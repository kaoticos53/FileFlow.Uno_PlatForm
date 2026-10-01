using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace FileFlow.Plugin.AI;

public static partial class MultimodalVlmClientEngine
{
    /// <summary>
    /// Retorna el esquema JSON (Structured Outputs) sugerido según el preset seleccionado, o null si no aplica.
    /// </summary>
    public static string? GetPresetJsonSchema(VlmTaskPreset preset)
    {
        return preset switch
        {
            VlmTaskPreset.ExtractInvoiceReceiptJson => """
            {
              "type": "object",
              "properties": {
                "tipo_documento": { "type": "string" },
                "numero_factura": { "type": "string" },
                "fecha_emision": { "type": "string" },
                "emisor_nombre": { "type": "string" },
                "emisor_cif_nif": { "type": "string" },
                "receptor_nombre": { "type": "string" },
                "receptor_cif_nif": { "type": "string" },
                "base_imponible": { "type": "number" },
                "porcentaje_iva": { "type": "number" },
                "cuota_iva": { "type": "number" },
                "importe_total": { "type": "number" },
                "divisa": { "type": "string" },
                "lineas_articulos": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "properties": {
                      "descripcion": { "type": "string" },
                      "cantidad": { "type": "number" },
                      "precio_unitario": { "type": "number" },
                      "importe": { "type": "number" }
                    },
                    "required": ["descripcion", "importe"]
                  }
                }
              },
              "required": ["tipo_documento", "importe_total"]
            }
            """,
            VlmTaskPreset.ClassifyAndTag => """
            {
              "type": "object",
              "properties": {
                "categoria": { "type": "string" },
                "confianza_aproximada": { "type": "number" },
                "etiquetas_descriptivas": { "type": "array", "items": { "type": "string" } },
                "motivo": { "type": "string" },
                "archivo": { "type": "string" }
              },
              "required": ["categoria", "confianza_aproximada", "etiquetas_descriptivas", "motivo"]
            }
            """,
            VlmTaskPreset.QualityInspection => """
            {
              "type": "object",
              "properties": {
                "es_valido_para_tramite": { "type": "boolean" },
                "legibilidad": { "type": "string" },
                "tiene_firma": { "type": "boolean" },
                "tiene_sello": { "type": "boolean" },
                "defectos_detectados": { "type": "array", "items": { "type": "string" } },
                "recomendacion": { "type": "string" }
              },
              "required": ["es_valido_para_tramite", "legibilidad", "tiene_firma", "tiene_sello", "defectos_detectados", "recomendacion"]
            }
            """,
            _ => null
        };
    }

    /// <summary>
    /// Retorna los prompts de sistema y usuario sugeridos según el preset seleccionado.
    /// </summary>
    public static (string SystemPrompt, string UserPrompt) GetPresetPrompts(VlmTaskPreset preset, string targetLanguage = "Español")
    {
        return preset switch
        {
            VlmTaskPreset.ExtractInvoiceReceiptJson => (
                "Eres un asistente contable y fiscal experto. Analiza la imagen del documento (factura, recibo, ticket o albarán) y extrae sus datos fiscales y económicos.\n" +
                "REGLA CRÍTICA DE ESTRUCTURA: Debes responder EXCLUSIVAMENTE con un único objeto JSON válido que respete de forma exacta e inmutable la siguiente estructura de campos (no inventes, no traduzcas ni renombres las claves; usa null o 0.00 si no se encuentra el dato):\n" +
                "{\n" +
                "  \"tipo_documento\": \"Factura | Recibo | Ticket | Albaran\",\n" +
                "  \"numero_factura\": \"string\",\n" +
                "  \"fecha_emision\": \"YYYY-MM-DD\",\n" +
                "  \"emisor_nombre\": \"string\",\n" +
                "  \"emisor_cif_nif\": \"string\",\n" +
                "  \"receptor_nombre\": \"string\",\n" +
                "  \"receptor_cif_nif\": \"string\",\n" +
                "  \"base_imponible\": 0.00,\n" +
                "  \"porcentaje_iva\": 21.00,\n" +
                "  \"cuota_iva\": 0.00,\n" +
                "  \"importe_total\": 0.00,\n" +
                "  \"divisa\": \"EUR | USD | GBP\",\n" +
                "  \"lineas_articulos\": [\n" +
                "    {\n" +
                "      \"descripcion\": \"string\",\n" +
                "      \"cantidad\": 1.0,\n" +
                "      \"precio_unitario\": 0.00,\n" +
                "      \"importe\": 0.00\n" +
                "    }\n" +
                "  ]\n" +
                "}\n" +
                "No agregues texto explicativo, comentarios ni bloques markdown fuera del JSON.",
                "Por favor, analiza este documento y extrae todos sus datos contables y fiscales completando estrictamente el esquema JSON requerido."
            ),
            VlmTaskPreset.DocumentOcrAndSummary => (
                "Eres un analista documental experto. Transcribe con fidelidad el texto visible en la imagen y a continuación elabora un resumen ejecutivo destacando los puntos y conclusiones principales en " + targetLanguage + ".\n" +
                "Si la salida requerida es JSON, utiliza obligatoriamente las siguientes claves inmutables: {\"texto_transcrito\": \"string\", \"resumen_ejecutivo\": \"string\", \"puntos_clave\": [\"string\"], \"idioma_detectado\": \"string\"}.",
                "Transcribe el texto visible de esta imagen o documento escaneado y genera un resumen ejecutivo claro y conciso."
            ),
            VlmTaskPreset.TranslateDocument => (
                $"Eres un traductor profesional multilingüe. Lee todo el contenido textual visible en la imagen y tradúcelo fielmente al {targetLanguage}. Preserva el formato de párrafos, listas y encabezados en Markdown.",
                $"Traduce todo el texto visible de esta imagen directamente al {targetLanguage} manteniendo el estilo y disposición original."
            ),
            VlmTaskPreset.ClassifyAndTag => (
                "Eres un clasificador de visión computacional y catalogación digital. Analiza la imagen y clasifícala en una de las siguientes categorías principales: " +
                "['Documento_Legal', 'Factura_Recibo', 'Documento_Identidad', 'Fotografia_Retrato', 'Fotografia_Paisaje', 'Captura_Pantalla_UI', 'Ilustracion_Dibujo', 'Otro'].\n" +
                "REGLA CRÍTICA DE ESTRUCTURA: Debes responder EXCLUSIVAMENTE con un único objeto JSON válido con estas claves inmutables en minúsculas:\n" +
                "{\n" +
                "  \"categoria\": \"string\",\n" +
                "  \"confianza_aproximada\": 0.95,\n" +
                "  \"etiquetas_descriptivas\": [\"tag1\", \"tag2\"],\n" +
                "  \"motivo\": \"string\",\n" +
                "  \"archivo\": \"string\"\n" +
                "}\n" +
                "No agregues explicaciones fuera del JSON.",
                "Clasifica esta imagen, asigna etiquetas descriptivas y explica brevemente el motivo."
            ),
            VlmTaskPreset.QualityInspection => (
                "Eres un auditor de calidad documental y fotográfica. Inspecciona minuciosamente la imagen y evalúa formalmente su validez.\n" +
                "REGLA CRÍTICA DE ESTRUCTURA: Debes responder EXCLUSIVAMENTE con un único objeto JSON válido con estas claves inmutables en minúsculas:\n" +
                "{\n" +
                "  \"es_valido_para_tramite\": true,\n" +
                "  \"legibilidad\": \"Excelente | Aceptable | Deficiente | Ilegible\",\n" +
                "  \"tiene_firma\": false,\n" +
                "  \"tiene_sello\": false,\n" +
                "  \"defectos_detectados\": [\"string\"],\n" +
                "  \"recomendacion\": \"string\"\n" +
                "}\n" +
                "No agregues texto explicativo fuera del JSON.",
                "Realiza una inspección exhaustiva de calidad y validez formal sobre esta imagen o documento escaneado respetando el esquema JSON requerido."
            ),
            VlmTaskPreset.CustomPrompt => (
                "Eres un asistente de inteligencia artificial visual multimodal preciso, conciso y objetivo.",
                "Describe detalladamente qué contiene esta imagen y extrae la información relevante."
            ),
            _ => (
                "Eres un asistente visual multimodal.",
                "Analiza la imagen adjunta."
            )
        };
    }

    /// <summary>
    /// Codifica una imagen ImageSharp en una URI de datos Base64 JPEG optimizada para envío HTTP a modelos VLM,
    /// aplicando un reescalado bicúbico proporcional si sobrepasa la dimensión máxima configurada.
    /// </summary>
    public static string PrepareImageAsBase64Jpeg(Image<Rgb24> image, int maxDimension = 1024)
    {
        ArgumentNullException.ThrowIfNull(image);

        int origW = image.Width;
        int origH = image.Height;
        int maxSide = Math.Max(origW, origH);

        Image<Rgb24> processImage = image;
        bool isCloned = false;

        if (maxSide > maxDimension && maxDimension >= 256)
        {
            float scale = (float)maxDimension / maxSide;
            int newW = Math.Max(1, (int)Math.Round(origW * scale));
            int newH = Math.Max(1, (int)Math.Round(origH * scale));

            processImage = image.Clone(ctx => ctx.Resize(newW, newH, KnownResamplers.Bicubic));
            isCloned = true;
        }

        try
        {
            using var ms = new MemoryStream();
            var encoder = new JpegEncoder
            {
                Quality = 85
            };
            processImage.SaveAsJpeg(ms, encoder);
            string base64 = Convert.ToBase64String(ms.ToArray());
            return "data:image/jpeg;base64," + base64;
        }
        finally
        {
            if (isCloned)
            {
                processImage.Dispose();
            }
        }
    }
}
