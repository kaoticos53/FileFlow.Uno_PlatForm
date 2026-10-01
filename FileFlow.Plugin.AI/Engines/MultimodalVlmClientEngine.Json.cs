using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileFlow.Sdk.Serialization;

namespace FileFlow.Plugin.AI;

public static partial class MultimodalVlmClientEngine
{
    /// <summary>
    /// Intenta extraer un bloque JSON válido de la respuesta, limpiando etiquetas markdown si estuvieran presentes.
    /// </summary>
    public static string? TryExtractValidJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        string trimmed = text.Trim();

        // 1. Si el texto completo es JSON directo
        if ((trimmed.StartsWith('{') && trimmed.EndsWith('}')) || (trimmed.StartsWith('[') && trimmed.EndsWith(']')))
        {
            if (IsValidJson(trimmed)) return JsonDefaults.FormatDetailsForDisplay(trimmed);
        }

        // 2. Extraer bloques de código ```json ... ```
        var match = JsonBlockRegex().Match(trimmed);
        if (match.Success)
        {
            string candidate = match.Groups[1].Value.Trim();
            if (IsValidJson(candidate)) return JsonDefaults.FormatDetailsForDisplay(candidate);
        }

        // 3. Buscar el primer '{' y el último '}'
        int firstBrace = trimmed.IndexOf('{');
        int lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            string candidate = trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
            if (IsValidJson(candidate)) return JsonDefaults.FormatDetailsForDisplay(candidate);
        }

        return null;
    }

    private static bool IsValidJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryExtractCategory(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("categoria", out var c1)) return c1.GetString();
                if (root.TryGetProperty("category", out var c2)) return c2.GetString();
                if (root.TryGetProperty("type", out var c3)) return c3.GetString();
            }
        }
        catch
        {
            // No es JSON estructurado
        }

        return null;
    }

    [GeneratedRegex(@"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase)]
    private static partial Regex JsonBlockRegex();
}
