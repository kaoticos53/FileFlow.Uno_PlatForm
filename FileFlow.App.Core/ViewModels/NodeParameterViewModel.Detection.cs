using FileFlow.Sdk;

namespace FileFlow.App.ViewModels;

public partial class NodeParameterViewModel
{
    private ParameterEditorType DetectEditorType()
    {
        if (DetectIsFileVersion(Key)) return ParameterEditorType.FileVersionSelector;
        if (DetectIsFolderPath(Key)) return ParameterEditorType.FolderPath;
        if (DetectIsFilePath(Key)) return ParameterEditorType.FilePath;
        if (Key.Equals("PasswordList", StringComparison.OrdinalIgnoreCase)) return ParameterEditorType.PasswordList;
        if (Key.Equals("Preset", StringComparison.OrdinalIgnoreCase)) return ParameterEditorType.MediaPreset;
        if (DetectIsMultiLine(Key)) return ParameterEditorType.MultiLineText;
        return ParameterEditorType.Text;
    }

    private static bool DetectIsFileVersion(string key) =>
        key.Equals("TargetFile", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("CandidateA", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("CandidateB", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("TrueFile", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("FalseFile", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("FileVersion", StringComparison.OrdinalIgnoreCase);

    private static string GetDefaultDisplayName(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return key;

        // Si la clave ya tiene formato PascalCase o camelCase, se puede formatear con espacios
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < key.Length; i++)
        {
            if (i > 0 && char.IsUpper(key[i]) && (!char.IsUpper(key[i - 1]) || (i + 1 < key.Length && !char.IsUpper(key[i + 1]))))
            {
                sb.Append(' ');
            }
            sb.Append(key[i]);
        }
        return sb.ToString();
    }

    private static bool DetectIsFolderPath(string key)
    {
        var k = key.ToLowerInvariant();
        if (k.Contains("file")) return false;
        return k.Contains("path") || k.Contains("folder") || k.Contains("dir") || k.Contains("destination") || k.Contains("source") || k.Contains("output");
    }

    private static bool DetectIsFilePath(string key)
    {
        var k = key.ToLowerInvariant();
        return k.Contains("file");
    }

    private static bool DetectIsMultiLine(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var k = key.ToLowerInvariant();
        return k.Contains("prompt") || k.Contains("template") || k.Contains("query") || k.Contains("sql") ||
               k.Contains("script") || k.Contains("instructions") || k.Contains("headers") || k.Contains("rules") ||
               k.Contains("labels") || k.Contains("candidatelabels") || k.Contains("body") || k.Contains("message");
    }

    private static List<string> DetectOptionsForKey(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "actiontype" => ["Keep", "MoveToRecycleBin", "MoveToQuarantine", "PermanentDelete"],
            "conflictstrategy" => ["Overwrite", "Skip", "RenameIncremental"],
            "collisionstrategy" => ["AutoIncrement", "Overwrite", "Skip", "Fail"],
            "renamemode" => ["Virtual", "DirectInPlace"],
            "targetformat" => ["WebP", "Jpeg", "Png"],
            "loglevel" => ["Information", "Warning", "Error", "Debug", "Critical"],
            "emitmode" => ["FilesOnly", "DirectoriesOnly", "FilesAndDirectories"],
            "casetransformation" => ["None", "Lowercase", "Uppercase", "TitleCase"],
            "operation" => ["Move", "Copy"],
            "algorithm" => ["SHA256", "MD5", "SHA512", "SHA1"],
            "operator" => [">", ">=", "<", "<=", "==", "!=", "Contains"],
            "hashmetadatakey" => ["Hash:SHA256", "Hash:MD5", "Hash:SHA512", "Hash:SHA1", "Hash"],
            "archiveformat" => ["ZIP", "TAR", "GZ", "7Z"],
            "compressiontype" => ["Deflate", "Store", "LZMA", "BZip2"],
            "preset" => ["Convertir 1080p H.264 (Universal MP4)", "Convertir 720p H.264 (MP4 Rápido)", "Convertir 4K H.265 / HEVC", "Extraer Audio MP3", "Extraer Audio AAC (M4A)", "Extraer Audio FLAC Lossless", "Convertir a GIF Animado", "WebM VP9 Open Video", "Móvil Ultra-Comprimido H.264", "Personalizado / Argumentos Libres"],
            "reportformat" => ["HTML", "Markdown", "Text", "JSON", "CSV"],
            "reportscope" => ["Consolidated", "PerFile", "Both"],
            "groupby" => ["Directory", "Flat", "Extension", "Status"],
            "theme" => ["ModernDark", "CleanLight"],
            _ => []
        };
    }
}
