using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FileFlow.Sdk.Serialization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FileFlow.Plugin.AI;

/// <summary>
/// Presets de tareas comunes para modelos multimodales de visión y lenguaje (VLM).
/// </summary>
public enum VlmTaskPreset
{
    ExtractInvoiceReceiptJson,
    DocumentOcrAndSummary,
    TranslateDocument,
    ClassifyAndTag,
    QualityInspection,
    CustomPrompt
}

/// <summary>
/// Resultado de inferencia multimodal obtenido del servidor VLM.
/// </summary>
public record VlmInferenceResult(
    string RawText,
    string? ExtractedJson,
    string? DetectedCategory,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    long DurationMs,
    string ModelUsed);

/// <summary>
/// Motor cliente HTTP resiliente para interactuar con servidores locales (LM Studio, Ollama)
/// o remotos compatibles con la API OpenAI Chat Completions para modelos de Visión-Lenguaje (Qwen2.5-VL, Llama-3.2-Vision, etc.).
/// </summary>
public static partial class MultimodalVlmClientEngine
{
    private static readonly HttpClient DefaultHttpClient = CreateDefaultHttpClient();

    /// <summary>
    /// Escala (en porcentaje, 100 = producción) de los tiempos de espera del motor: el backoff de
    /// reintentos ante errores transitorios (1,5 s y 2 s por intento) y el enfriamiento de 250 ms que
    /// se aplica a los endpoints locales para liberar la KV Cache del slot.
    ///
    /// Es <c>internal</c> a propósito: sólo el ensamblado de pruebas lo toca. Las pruebas del nodo
    /// ejercitan la política de reintento contra servidores HTTP simulados, donde no hay slot real que
    /// enfriar ni sobrecarga que esperar; con la escala al 0% esa suite pasa de ~9 s a ~2 s sin tocar
    /// una sola línea de la política que se quiere probar. Valor por defecto 100: el comportamiento en
    /// producción no cambia ni un milisegundo.
    /// </summary>
    internal static int RetryBackoffScalePercent { get; set; } = 100;

    /// <summary>Aplica la escala de pruebas a un intervalo de espera.</summary>
    private static TimeSpan Scaled(TimeSpan delay) =>
        TimeSpan.FromMilliseconds(delay.TotalMilliseconds * RetryBackoffScalePercent / 100.0);

    /// <summary>
    /// Semáforos de concurrencia por host/endpoint para evitar saturar la memoria VRAM y los slots
    /// de inferencia de servidores locales de VLM (LM Studio, Ollama) durante ejecuciones paralelas en pipeline.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_endpointThrottles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registro en memoria de endpoints/modelos que han rechazado el parámetro 'response_format' (ej. Error 400).
    /// Evita reenviar 'response_format' en subsecuentes imágenes del mismo lote.
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> s_unsupportedResponseFormatCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registro en memoria de endpoints/modelos que han rechazado 'json_schema' en 'response_format' pero admiten 'json_object'.
    /// </summary>
    private static readonly ConcurrentDictionary<string, bool> s_unsupportedJsonSchemaCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cortocircuito (circuit breaker) por endpoint inalcanzable: un servidor local apagado rechaza la conexión
    /// al instante, y un pipeline de cientos de imágenes reintentaba contra él en vano. Con el endpoint marcado
    /// como caído, las peticiones siguientes fallan de inmediato con el mismo mensaje de ayuda, sin abrir una sola
    /// conexión. Esto es lo que convierte cientos de fallos de socket (y sus tareas fallidas no observadas) en uno solo.
    ///
    /// Es <c>internal</c> a propósito: sólo el ensamblado de pruebas ajusta el enfriamiento y lo reinicia.
    /// </summary>
    internal static TimeSpan UnreachableEndpointCooldown { get; set; } = TimeSpan.FromSeconds(15);

    private static readonly ConcurrentDictionary<string, DateTimeOffset> s_unreachableEndpoints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Limpia el cortocircuito de todos los endpoints (usado por las pruebas).</summary>
    internal static void ResetUnreachableEndpoints() => s_unreachableEndpoints.Clear();

    private static bool IsEndpointCoolingDown(string endpoint) =>
        s_unreachableEndpoints.TryGetValue(endpoint, out var until) && DateTimeOffset.UtcNow < until;

    private static void MarkEndpointUnreachable(string endpoint) =>
        s_unreachableEndpoints[endpoint] = DateTimeOffset.UtcNow + UnreachableEndpointCooldown;

    private static void ClearEndpointUnreachable(string endpoint) =>
        s_unreachableEndpoints.TryRemove(endpoint, out _);

    /// <summary>
    /// Determina si la excepción de transporte corresponde a un servidor apagado o inalcanzable
    /// (y por tanto a un fallo que no se arregla reintentando).
    /// </summary>
    private static bool IsUnreachableServerFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket &&
                socket.SocketErrorCode is SocketError.ConnectionRefused
                    or SocketError.HostNotFound
                    or SocketError.HostUnreachable
                    or SocketError.NetworkUnreachable
                    or SocketError.TimedOut)
            {
                return true;
            }
        }

        return false;
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = System.Net.DecompressionMethods.All
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FileFlowStudio-VLM/1.0");
        return client;
    }

    // Lógica de presets, prompts sugeridos y preparación/escalado de imágenes Base64
    // modularizada en MultimodalVlmClientEngine.Prompts.cs.


    /// <summary>
    /// Ejecuta una inferencia multimodal contra un servidor compatible con la API de OpenAI (LM Studio, Ollama, etc.).
    /// </summary>
    public static async Task<VlmInferenceResult> ExecuteChatCompletionAsync(
        string endpointUrl,
        string modelName,
        string? apiKey,
        string systemPrompt,
        string userPrompt,
        string base64ImageDataUrl,
        double temperature = 0.1,
        int maxTokens = 2048,
        bool forceJsonOutput = false,
        string? jsonSchema = null,
        TimeSpan? timeout = null,
        HttpClient? customHttpClient = null,
        int concurrencyLimit = 0,
        CancellationToken cancellationToken = default)
    {
        var client = customHttpClient ?? DefaultHttpClient;
        var sw = Stopwatch.StartNew();

        // Normalizar endpoint URL: asegurar formato http://host:port/v1/chat/completions
        string cleanEndpoint = (endpointUrl ?? "http://localhost:1234/v1").Trim().TrimEnd('/');
        if (!cleanEndpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            cleanEndpoint += "/chat/completions";
        }

        // Servidor local apagado detectado hace poco: fallar de inmediato en lugar de reintentar sin sentido.
        if (IsEndpointCoolingDown(cleanEndpoint))
        {
            throw new InvalidOperationException(
                $"No se pudo conectar con el servidor VLM en '{cleanEndpoint}'. Asegúrate de que LM Studio o el servidor local esté en ejecución: " +
                $"el endpoint rechazó la conexión hace menos de {UnreachableEndpointCooldown.TotalSeconds:0} s y se omite el reintento para no inundar a un servidor apagado.");
        }

        string effectiveModel = !string.IsNullOrWhiteSpace(modelName) ? modelName : "qwen2.5-vl-7b-instruct";
        string unsupportedCacheKey = $"{cleanEndpoint}::{effectiveModel}";

        // Construir payload JSON compatible con OpenAI Chat Completions Multimodal
        var userContentList = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = userPrompt
            },
            new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject
                {
                    ["url"] = base64ImageDataUrl
                }
            }
        };

        var messagesArray = new JsonArray();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messagesArray.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemPrompt
            });
        }

        messagesArray.Add(new JsonObject
        {
            ["role"] = "user",
            ["content"] = userContentList
        });

        var requestBody = new JsonObject
        {
            ["model"] = effectiveModel,
            ["messages"] = messagesArray,
            ["temperature"] = Math.Clamp(temperature, 0.0, 1.0),
            ["max_tokens"] = Math.Max(64, maxTokens),
            ["stream"] = false
        };

        // Configuración de response_format determinista (Structured Outputs con json_schema o json_object)
        bool shouldSendResponseFormat = forceJsonOutput && !s_unsupportedResponseFormatCache.ContainsKey(unsupportedCacheKey);
        if (shouldSendResponseFormat)
        {
            bool tryJsonSchema = !string.IsNullOrWhiteSpace(jsonSchema) && !s_unsupportedJsonSchemaCache.ContainsKey(unsupportedCacheKey);
            if (tryJsonSchema)
            {
                try
                {
                    var schemaNode = JsonNode.Parse(jsonSchema!);
                    if (schemaNode != null)
                    {
                        requestBody["response_format"] = new JsonObject
                        {
                            ["type"] = "json_schema",
                            ["json_schema"] = new JsonObject
                            {
                                ["name"] = "vlm_output_schema",
                                ["strict"] = true,
                                ["schema"] = schemaNode
                            }
                        };
                    }
                    else
                    {
                        requestBody["response_format"] = new JsonObject { ["type"] = "json_object" };
                    }
                }
                catch
                {
                    requestBody["response_format"] = new JsonObject { ["type"] = "json_object" };
                }
            }
            else
            {
                requestBody["response_format"] = new JsonObject
                {
                    ["type"] = "json_object"
                };
            }
        }

        // Obtener el semáforo de concurrencia adecuado según el host y la concurrencia configurada
        var throttle = GetThrottleForEndpoint(cleanEndpoint, concurrencyLimit);
        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout.HasValue && timeout.Value > TimeSpan.Zero)
        {
            cts.CancelAfter(timeout.Value);
        }

        HttpResponseMessage response;
        string responseContent;

        try
        {
            const int maxAttempts = 3;
            int currentAttempt = 0;

            while (true)
            {
                currentAttempt++;
                string requestJson = requestBody.ToJsonString();

                using var requestMessage = new HttpRequestMessage(HttpMethod.Post, cleanEndpoint)
                {
                    Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                };

                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                }

                try
                {
                    response = await client.SendAsync(requestMessage, cts.Token).ConfigureAwait(false);
                }
                catch (HttpRequestException ex)
                {
                    if (IsUnreachableServerFailure(ex))
                    {
                        // Un servidor apagado no se arregla reintentando: cada reintento es otra conexión rechazada
                        // (y, en pipelines largos, otra tarea fallida que el finalizador acaba reportando).
                        MarkEndpointUnreachable(cleanEndpoint);
                        throw new InvalidOperationException(
                            $"No se pudo conectar con el servidor VLM en '{cleanEndpoint}'. Asegúrate de que LM Studio o el servidor local esté en ejecución: {ex.Message}", ex);
                    }

                    if (currentAttempt < maxAttempts && !cts.IsCancellationRequested)
                    {
                        await Task.Delay(Scaled(TimeSpan.FromSeconds(1.5 * currentAttempt)), cts.Token).ConfigureAwait(false);
                        continue;
                    }
                    throw new InvalidOperationException($"No se pudo conectar con el servidor VLM en '{cleanEndpoint}'. Asegúrate de que LM Studio o el servidor local esté en ejecución: {ex.Message}", ex);
                }

                responseContent = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                // Manejo de Error 400 por rechazo de 'response_format' o 'json_schema':
                // Si falla con json_schema, degradamos a json_object. Si falla con json_object, omitimos response_format.
                if (response.StatusCode == HttpStatusCode.BadRequest && requestBody.ContainsKey("response_format"))
                {
                    var currentRf = requestBody["response_format"] as JsonObject;
                    string currentType = currentRf?["type"]?.ToString() ?? string.Empty;

                    if (string.Equals(currentType, "json_schema", StringComparison.OrdinalIgnoreCase))
                    {
                        s_unsupportedJsonSchemaCache[unsupportedCacheKey] = true;
                        requestBody["response_format"] = new JsonObject { ["type"] = "json_object" };
                        response.Dispose();
                        continue;
                    }

                    if (responseContent.Contains("response_format", StringComparison.OrdinalIgnoreCase) ||
                        responseContent.Contains("json_schema", StringComparison.OrdinalIgnoreCase) ||
                        responseContent.Contains("json_object", StringComparison.OrdinalIgnoreCase) ||
                        responseContent.Contains("schema", StringComparison.OrdinalIgnoreCase))
                    {
                        s_unsupportedResponseFormatCache[unsupportedCacheKey] = true;
                        requestBody.Remove("response_format");
                        response.Dispose();
                        continue;
                    }
                }

                // Manejo de errores transitorios 5xx (500 Channel Error, 502, 503, 504) o 400 por colapso de slot / canal en LM Studio
                // Ocurren típicamente en LM Studio cuando un slot de inferencia se reinicia o se recupera de sobrecarga.
                bool isTransient = ((int)response.StatusCode >= 500 && (int)response.StatusCode <= 504) ||
                                   (response.StatusCode == HttpStatusCode.BadRequest &&
                                    (responseContent.Contains("channel", StringComparison.OrdinalIgnoreCase) ||
                                     responseContent.Contains("overload", StringComparison.OrdinalIgnoreCase) ||
                                     responseContent.Contains("busy", StringComparison.OrdinalIgnoreCase) ||
                                     responseContent.Contains("terminated", StringComparison.OrdinalIgnoreCase) ||
                                     responseContent.Contains("aborted", StringComparison.OrdinalIgnoreCase) ||
                                     responseContent.Contains("slot", StringComparison.OrdinalIgnoreCase)));

                if (isTransient && currentAttempt < maxAttempts && !cts.IsCancellationRequested)
                {
                    response.Dispose();
                    await Task.Delay(Scaled(TimeSpan.FromSeconds(2.0 * currentAttempt)), cts.Token).ConfigureAwait(false);
                    continue;
                }

                break;
            }
        }
        finally
        {
            // Breve enfriamiento (cooldown) en endpoints locales para permitir que llama-server / LM Studio
            // libere completamente la memoria KV Cache del slot antes de admitir la siguiente inferencia en pipeline.
            if (IsLocalEndpoint(cleanEndpoint))
            {
                try
                {
                    await Task.Delay(Scaled(TimeSpan.FromMilliseconds(250)), CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Ignorar cancelación en cooldown
                }
            }
            throttle.Release();
        }

        // Cualquier respuesta HTTP (incluido un error 4xx/5xx) demuestra que el servidor está en marcha.
        ClearEndpointUnreachable(cleanEndpoint);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"El servidor VLM respondió con código {(int)response.StatusCode} ({response.StatusCode}): {responseContent}");
        }

        // Parsear respuesta
        using var doc = JsonDocument.Parse(responseContent);
        var root = doc.RootElement;

        string assistantText = string.Empty;
        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var contentElem))
            {
                assistantText = JsonDefaults.UnescapeUnicode(contentElem.GetString() ?? string.Empty);
            }
        }

        int promptTokens = 0;
        int completionTokens = 0;
        int totalTokens = 0;

        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct)) completionTokens = ct.GetInt32();
            if (usage.TryGetProperty("total_tokens", out var tt)) totalTokens = tt.GetInt32();
        }

        sw.Stop();

        // Extraer y sanitizar JSON si existe
        string? extractedJson = TryExtractValidJson(assistantText);
        string? detectedCategory = TryExtractCategory(extractedJson ?? assistantText);

        return new VlmInferenceResult(
            RawText: assistantText,
            ExtractedJson: extractedJson,
            DetectedCategory: detectedCategory,
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TotalTokens: totalTokens > 0 ? totalTokens : promptTokens + completionTokens,
            DurationMs: sw.ElapsedMilliseconds,
            ModelUsed: modelName);
    }

    // Lógica de extracción de bloques JSON, parsing y expresiones regulares
    // modularizada en MultimodalVlmClientEngine.Json.cs.


    /// <summary>
    /// Determina si una URL corresponde a un servidor local (localhost, 127.0.0.1, ::1 o puertos locales 1234/11434).
    /// </summary>
    public static bool IsLocalEndpoint(string endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl)) return false;

        try
        {
            var uri = new Uri(endpointUrl);
            return uri.IsLoopback ||
                   string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return endpointUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                   endpointUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                   endpointUrl.Contains("1234", StringComparison.OrdinalIgnoreCase) ||
                   endpointUrl.Contains("11434", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Retorna un semáforo de limitación de concurrencia adecuado para el endpoint dado.
    /// Si el usuario configuró una concurrencia específica, se respeta dicha capacidad.
    /// Por defecto, para servidores locales se usa 1 (salvo configuración explícita) y para remotos 4.
    /// </summary>
    private static SemaphoreSlim GetThrottleForEndpoint(string endpointUrl, int requestedConcurrency = 0)
    {
        string hostKey;
        bool isLocal = IsLocalEndpoint(endpointUrl);

        try
        {
            var uri = new Uri(endpointUrl);
            hostKey = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
        }
        catch
        {
            hostKey = endpointUrl;
        }

        int count = requestedConcurrency > 0 ? requestedConcurrency : (isLocal ? 1 : 4);
        string throttleKey = $"{hostKey}::{count}";
        return s_endpointThrottles.GetOrAdd(throttleKey, _ => new SemaphoreSlim(count, count));
    }
}
