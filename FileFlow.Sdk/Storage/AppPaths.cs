using System.IO;
using System.Runtime.InteropServices;

namespace FileFlow.Sdk.Storage;

/// <summary>
/// Proveedor centralizado de rutas del sistema de archivos para FileFlow Studio.
/// Sigue las especificaciones modernas de cada plataforma (Windows Roaming vs LocalAppData,
/// Linux XDG Base Directory Specification, macOS Library) y soporta modo portable 100% hermético.
/// </summary>
public static class AppPaths
{
    public const string AppName = "FileFlowStudio";
    private static readonly string AppBaseDirectory = AppContext.BaseDirectory;
    private static string? _customDataDirectory;
    private static readonly Lock _lock = new();

    /// <summary>
    /// Indica si la aplicación se está ejecutando en modo portable autónomo.
    /// Se activa automáticamente si existe un archivo marcador 'portable.dat' o '.portable' junto al ejecutable,
    /// o mediante la variable de entorno FILEFLOW_PORTABLE=1.
    /// </summary>
    public static bool IsPortableMode
    {
        get
        {
            if (!string.IsNullOrEmpty(_customDataDirectory)) return true;

            string envPortable = Environment.GetEnvironmentVariable("FILEFLOW_PORTABLE") ?? string.Empty;
            if (envPortable.Equals("1", StringComparison.OrdinalIgnoreCase) || envPortable.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return File.Exists(Path.Combine(AppBaseDirectory, "portable.dat")) ||
                   File.Exists(Path.Combine(AppBaseDirectory, ".portable"));
        }
    }

    /// <summary>
    /// Permite forzar un directorio raíz de datos personalizado (útil para pruebas unitarias, perfiles y CLI).
    /// </summary>
    public static void SetCustomDataDirectory(string? customPath)
    {
        lock (_lock)
        {
            _customDataDirectory = string.IsNullOrWhiteSpace(customPath) ? null : customPath;
        }
    }

    /// <summary>
    /// Directorio raíz de configuración y preferencias del usuario (Roaming / XDG_CONFIG).
    /// En modo portable retorna AppBaseDir/data.
    /// </summary>
    public static string RootDirectory
    {
        get
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_customDataDirectory))
                {
                    return _customDataDirectory;
                }

                if (IsPortableMode)
                {
                    string portableData = Path.Combine(AppBaseDirectory, "data");
                    if (IsDirectoryWritable(portableData))
                    {
                        return portableData;
                    }
                }

                return ResolveDefaultConfigRoot();
            }
        }
    }

    /// <summary>
    /// Directorio raíz para datos locales de gran volumen, modelos, checkpoints y caché (LocalAppData / XDG_DATA / XDG_CACHE).
    /// En modo portable retorna AppBaseDir/data (completamente aislado).
    /// </summary>
    public static string LocalDataDirectory
    {
        get
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_customDataDirectory))
                {
                    return _customDataDirectory;
                }

                if (IsPortableMode)
                {
                    string portableData = Path.Combine(AppBaseDirectory, "data");
                    if (IsDirectoryWritable(portableData))
                    {
                        return portableData;
                    }
                }

                return ResolveDefaultLocalDataRoot();
            }
        }
    }

    private static string ResolveDefaultConfigRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string? xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdgConfig))
            {
                return Path.Combine(xdgConfig, "fileflow");
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".config", "fileflow");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", AppName);
        }

        // Windows (Roaming AppData)
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
    }

    private static string ResolveDefaultLocalDataRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string? xdgData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgData))
            {
                return Path.Combine(xdgData, "fileflow");
            }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".local", "share", "fileflow");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Caches", AppName);
        }

        // Windows (Local AppData)
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
    }

    /// <summary>
    /// Comprueba de forma segura y no destructiva si un directorio es accesible y escribible por el usuario actual.
    /// </summary>
    public static bool IsDirectoryWritable(string directoryPath)
    {
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            string testFile = Path.Combine(directoryPath, $".write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "write_test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // =========================================================================
    // SUBDIRECTORIOS ESTRUCTURADOS (CONFIG & PRESETS)
    // =========================================================================
    public static string ConfigDirectory => Path.Combine(RootDirectory, "config");
    public static string ThemesDirectory => Path.Combine(RootDirectory, "themes");
    public static string PresetsDirectory => Path.Combine(RootDirectory, "presets");
    public static string SamplesDirectory => Path.Combine(RootDirectory, "samples");
    public static string ScriptsDirectory => Path.Combine(RootDirectory, "scripts");
    public static string DataSetsDirectory => Path.Combine(RootDirectory, "datasets");

    // =========================================================================
    // SUBDIRECTORIOS ESTRUCTURADOS (LOCAL DATA, MODELS, CACHE & LOGS)
    // =========================================================================
    public static string ModelsDirectory => Path.Combine(LocalDataDirectory, "models");
    public static string CheckpointsDirectory => Path.Combine(LocalDataDirectory, "checkpoints");
    public static string PluginsDirectory => Path.Combine(LocalDataDirectory, "plugins");
    public static string LogsDirectory => Path.Combine(LocalDataDirectory, "logs");

    // =========================================================================
    // SALIDAS DE USUARIO Y TEMPORALES
    // =========================================================================

    /// <summary>
    /// Ruta de salida global por defecto utilizada por los flujos y variables del sistema.
    /// (Modo Portable: AppBaseDir/data/output, Modo Instalado: %USERPROFILE%/Documents/FileFlowStudio/Output).
    /// </summary>
    public static string DefaultGlobalOutputDir
    {
        get
        {
            if (IsPortableMode)
            {
                return Path.Combine(RootDirectory, "output");
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppName, "Output");
        }
    }

    /// <summary>
    /// Ruta de trabajo temporal por defecto para archivos intermedios generados por los nodos.
    /// (Modo Portable: AppBaseDir/data/temp, Modo Instalado: %TEMP%/FileFlowStudio/Temp).
    /// </summary>
    public static string DefaultTempDirectory
    {
        get
        {
            if (IsPortableMode)
            {
                return Path.Combine(RootDirectory, "temp");
            }
            return Path.Combine(Path.GetTempPath(), AppName, "Temp");
        }
    }

    /// <summary>
    /// Directorio que alberga los espacios de trabajo temporales acotados por ejecución (Runs/{ExecutionId}/).
    /// </summary>
    public static string RunsDirectory => Path.Combine(DefaultTempDirectory, "Runs");

    /// <summary>
    /// Limpia de forma segura y recursiva directorios y archivos temporales residuales o abandonados de ejecuciones anteriores.
    /// </summary>
    public static long CleanupStaleTempDirectories(TimeSpan? maxAge = null)
    {
        TimeSpan effectiveMaxAge = maxAge ?? TimeSpan.FromHours(2);
        DateTime thresholdUtc = DateTime.UtcNow - effectiveMaxAge;
        long totalBytesFreed = 0;

        string[] candidateRoots =
        [
            RunsDirectory,
            Path.Combine(DefaultTempDirectory, "intermediate"),
            Path.Combine(Path.GetTempPath(), "FileFlow_Sessions"),
            Path.Combine(Path.GetTempPath(), "FileFlow_MockData")
        ];

        foreach (var root in candidateRoots)
        {
            if (!Directory.Exists(root)) continue;

            try
            {
                foreach (var subDir in Directory.GetDirectories(root))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(subDir);
                        if (dirInfo.LastWriteTimeUtc < thresholdUtc || dirInfo.CreationTimeUtc < thresholdUtc)
                        {
                            long dirSize = GetDirectorySize(subDir);
                            Directory.Delete(subDir, recursive: true);
                            totalBytesFreed += dirSize;
                        }
                    }
                    catch { }
                }

                foreach (var file in Directory.GetFiles(root))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.LastWriteTimeUtc < thresholdUtc || fileInfo.CreationTimeUtc < thresholdUtc)
                        {
                            long size = fileInfo.Length;
                            File.Delete(file);
                            totalBytesFreed += size;
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return totalBytesFreed;
    }

    private static long GetDirectorySize(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return 0;
        try
        {
            return Directory.GetFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                .Sum(f => {
                    try { return new FileInfo(f).Length; } catch { return 0L; }
                });
        }
        catch
        {
            return 0;
        }
    }

    // =========================================================================
    // FICHEROS ESTÁNDAR DE CONFIGURACIÓN Y PERSISTENCIA
    // =========================================================================
    public static string UserPreferencesFile => Path.Combine(ConfigDirectory, "user_preferences.json");
    public static string ExternalToolsFile => Path.Combine(ConfigDirectory, "external_tools.json");
    public static string CustomThemesFile => Path.Combine(ThemesDirectory, "custom_themes.json");
    public static string RenamerPresetsFile => Path.Combine(PresetsDirectory, "renamer_presets.json");
    public static string MediaPresetsFile => Path.Combine(PresetsDirectory, "media_presets.json");
    public static string RegexLibraryFile => Path.Combine(PresetsDirectory, "regex_library.json");
    public static string DataSetsFile => Path.Combine(DataSetsDirectory, "synthetic_datasets.json");
    public static string RenamerSamplesFile => Path.Combine(SamplesDirectory, "renamer_samples.json");
    public static string CrashLogFile => Path.Combine(LogsDirectory, "crash.log");

    /// <summary>
    /// Resuelve una ruta que puede ser absoluta o relativa a la carpeta del ejecutable de la aplicación.
    /// Útil para herramientas portables como tools\ffmpeg\ffmpeg.exe.
    /// </summary>
    public static string ResolveApplicationPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        if (CrossPlatformPath.IsPathFullyQualified(path)) return path;

        return Path.GetFullPath(Path.Combine(AppBaseDirectory, path));
    }

    /// <summary>
    /// Garantiza la existencia de toda la jerarquía de directorios de datos y realiza
    /// la migración automática de cualquier fichero ubicado en carpetas heredadas.
    /// </summary>
    public static void EnsureDirectories()
    {
        string[] dirsToCreate =
        [
            RootDirectory,
            LocalDataDirectory,
            ConfigDirectory,
            ThemesDirectory,
            PresetsDirectory,
            SamplesDirectory,
            ScriptsDirectory,
            DataSetsDirectory,
            LogsDirectory,
            PluginsDirectory,
            ModelsDirectory,
            CheckpointsDirectory,
            DefaultTempDirectory
        ];

        foreach (var dir in dirsToCreate)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
            catch { }
        }

        try
        {
            if (!IsPortableMode)
            {
                MigrateLegacyLocations();
            }
        }
        catch { }
    }

    /// <summary>
    /// Migra de forma no destructiva ficheros existentes en ubicaciones heredadas (%AppData%/FileFlow/, %AppData%/FileFlowStudio/, etc.).
    /// </summary>
    private static void MigrateLegacyLocations()
    {
        try
        {
            string baseAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string baseLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            // 1. Migración desde %AppData%/FileFlow/ (nombre previo sin 'Studio')
            string legacyShortDir = Path.Combine(baseAppData, "FileFlow");
            if (Directory.Exists(legacyShortDir) && !string.Equals(legacyShortDir, RootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                MigrateFile(Path.Combine(legacyShortDir, "user_preferences.json"), UserPreferencesFile);
                MigrateFile(Path.Combine(legacyShortDir, "config", "user_preferences.json"), UserPreferencesFile);
                MigrateFile(Path.Combine(legacyShortDir, "external_tools.json"), ExternalToolsFile);
                MigrateFile(Path.Combine(legacyShortDir, "config", "external_tools.json"), ExternalToolsFile);
                MigrateFile(Path.Combine(legacyShortDir, "custom_themes.json"), CustomThemesFile);
                MigrateFile(Path.Combine(legacyShortDir, "themes", "custom_themes.json"), CustomThemesFile);
                MigrateFile(Path.Combine(legacyShortDir, "renamer_presets.json"), RenamerPresetsFile);
                MigrateFile(Path.Combine(legacyShortDir, "presets", "renamer_presets.json"), RenamerPresetsFile);
                MigrateFile(Path.Combine(legacyShortDir, "media_presets.json"), MediaPresetsFile);
                MigrateFile(Path.Combine(legacyShortDir, "presets", "media_presets.json"), MediaPresetsFile);
                MigrateFile(Path.Combine(legacyShortDir, "regex_library.json"), RegexLibraryFile);
                MigrateFile(Path.Combine(legacyShortDir, "presets", "regex_library.json"), RegexLibraryFile);
                MigrateFile(Path.Combine(legacyShortDir, "renamer_samples.json"), RenamerSamplesFile);
                MigrateFile(Path.Combine(legacyShortDir, "samples", "renamer_samples.json"), RenamerSamplesFile);
                MigrateFile(Path.Combine(legacyShortDir, "crash.log"), CrashLogFile);
                MigrateFile(Path.Combine(legacyShortDir, "logs", "crash.log"), CrashLogFile);

                // Migrar SyntheticDataSets
                string legacySets = Path.Combine(legacyShortDir, "SyntheticDataSets");
                if (Directory.Exists(legacySets))
                {
                    MigrateDirectoryContents(legacySets, DataSetsDirectory);
                }

                // Migrar models
                string legacyModels = Path.Combine(legacyShortDir, "models");
                if (Directory.Exists(legacyModels) && !string.Equals(legacyModels, ModelsDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    MigrateDirectoryContents(legacyModels, ModelsDirectory);
                }

                // Migrar scripts
                string legacyScripts = Path.Combine(legacyShortDir, "scripts");
                if (Directory.Exists(legacyScripts) && !string.Equals(legacyScripts, ScriptsDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    MigrateDirectoryContents(legacyScripts, ScriptsDirectory);
                }
            }

            // 2. Migración desde la raíz de RootDirectory hacia las nuevas subcarpetas estructuradas
            MigrateFile(Path.Combine(RootDirectory, "user_preferences.json"), UserPreferencesFile);
            MigrateFile(Path.Combine(RootDirectory, "external_tools.json"), ExternalToolsFile);
            MigrateFile(Path.Combine(RootDirectory, "custom_themes.json"), CustomThemesFile);
            MigrateFile(Path.Combine(RootDirectory, "renamer_presets.json"), RenamerPresetsFile);
            MigrateFile(Path.Combine(RootDirectory, "media_presets.json"), MediaPresetsFile);
            MigrateFile(Path.Combine(RootDirectory, "regex_library.json"), RegexLibraryFile);
            MigrateFile(Path.Combine(RootDirectory, "renamer_samples.json"), RenamerSamplesFile);
            MigrateFile(Path.Combine(RootDirectory, "crash.log"), CrashLogFile);

            // 3. Migración de SyntheticDataSets en RootDirectory
            string rootLegacySets = Path.Combine(RootDirectory, "SyntheticDataSets");
            if (Directory.Exists(rootLegacySets))
            {
                MigrateDirectoryContents(rootLegacySets, DataSetsDirectory);
            }

            // 4. Migración de checkpoints en LocalAppData heredado
            string oldCheckpoints = Path.Combine(baseLocalAppData, "FileFlowStudio", "checkpoints");
            if (Directory.Exists(oldCheckpoints) && !string.Equals(oldCheckpoints, CheckpointsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                MigrateDirectoryContents(oldCheckpoints, CheckpointsDirectory);
            }
        }
        catch { }
    }

    private static void MigrateFile(string sourcePath, string targetPath)
    {
        if (File.Exists(sourcePath) && !File.Exists(targetPath))
        {
            try
            {
                string? destDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                File.Copy(sourcePath, targetPath, false);
            }
            catch { }
        }
    }

    private static void MigrateDirectoryContents(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        try
        {
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                if (!File.Exists(destFile))
                {
                    try { File.Copy(file, destFile, false); } catch { }
                }
            }
        }
        catch { }
    }
}
