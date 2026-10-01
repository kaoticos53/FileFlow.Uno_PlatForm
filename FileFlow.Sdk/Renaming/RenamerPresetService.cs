using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileFlow.Sdk.Storage;

namespace FileFlow.Sdk.Renaming;

/// <summary>
/// Modelo de ajuste predefinido (Preset) para el motor de renombrado avanzado.
/// </summary>
public sealed record RenamerPreset
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = "General";
    public List<RenameMethodStep> Steps { get; init; } = [];
}

/// <summary>
/// Servicio de gestión de ajustes predefinidos (Presets) para AdvancedRenamer.
/// Soporta carga en cascada desde %AppData%, directorio Config/ y fallback en memoria.
/// </summary>
public static partial class RenamerPresetService
{
    /// <summary>
    /// Opciones de lectura y escritura de los pasos de renombrado. El conversor de enumeraciones <b>por nombre</b>
    /// es lo que hace legible un pipeline escrito a mano —un ejemplo del catálogo, un flujo editado fuera de la
    /// aplicación—: sin él, <c>"methodType": "NewName"</c> no se puede leer, la lectura falla entera y el
    /// renombrador cae en la plantilla por omisión (<c>{ParentDir}_{CreationDate:yyyyMMdd}_{FileNameNoExt}.{Ext}</c>),
    /// renombrando a un nombre que nadie configuró. Acepta también los números que escribe la aplicación, así que
    /// los dos dialectos conviven.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<RenamerPreset> GetBuiltinPresets()
    {
        var dict = new Dictionary<string, RenamerPreset>(StringComparer.OrdinalIgnoreCase);

        // 1. Fallback determinista en memoria (garantiza que todos los presets oficiales siempre existan)
        foreach (var p in GetFallbackPresets())
        {
            dict[p.Name] = p;
        }

        // 2. Intentar cargar/actualizar desde el directorio Config/ de la aplicación o plugin
        string[] candidatePaths =
        [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "renamer_presets.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins", "Config", "renamer_presets.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Config", "renamer_presets.json"),
            Path.Combine(AppContext.BaseDirectory, "Config", "renamer_presets.json")
        ];

        foreach (var path in candidatePaths.Distinct())
        {
            if (File.Exists(path))
            {
                var factoryPresets = TryLoadPresetsFromFile(path);
                if (factoryPresets != null)
                {
                    foreach (var fp in factoryPresets)
                    {
                        dict[fp.Name] = fp;
                    }
                }
            }
        }

        // 3. Cargar presets de usuario en %AppData%/FileFlow/presets/renamer_presets.json
        AppPaths.EnsureDirectories();
        string appDataFile = AppPaths.RenamerPresetsFile;
        if (File.Exists(appDataFile))
        {
            var userPresets = TryLoadPresetsFromFile(appDataFile);
            if (userPresets != null)
            {
                foreach (var up in userPresets)
                {
                    dict[up.Name] = up;
                }
            }
        }

        return dict.Values.ToList();
    }

    public static List<RenamerPreset>? TryLoadPresetsFromFile(string filePath)
    {
        try
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<List<RenamerPreset>>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string SerializePreset(RenamerPreset preset)
    {
        return JsonSerializer.Serialize(preset, JsonOptions);
    }

    public static RenamerPreset? DeserializePreset(string json)
    {
        return JsonSerializer.Deserialize<RenamerPreset>(json, JsonOptions);
    }

    public static string SerializeSteps(IReadOnlyList<RenameMethodStep> steps)
    {
        return JsonSerializer.Serialize(steps, JsonOptions);
    }

    public static List<RenameMethodStep> DeserializeSteps(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<List<RenameMethodStep>>(json, JsonOptions) ?? [];
    }

    // Catálogo determinista de presets de fábrica (Fotografía, Vídeo, Audio, Web/SEO, etc.)
    // modularizado en RenamerPresetService.Presets.cs.
}
