using System.IO;
using FileFlow.Sdk.Storage;
using FluentAssertions;
using Xunit;

namespace FileFlow.Tests.Unit.Sdk;

[Collection("AppPaths")]
public class AppPathsTests
{
    [Fact]
    public void AppPaths_ShouldExposeConsistentHierarchicalPaths()
    {
        // Assert
        AppPaths.RootDirectory.Should().EndWith("FileFlowStudio");
        AppPaths.ConfigDirectory.Should().Be(Path.Combine(AppPaths.RootDirectory, "config"));
        AppPaths.ThemesDirectory.Should().Be(Path.Combine(AppPaths.RootDirectory, "themes"));
        AppPaths.PresetsDirectory.Should().Be(Path.Combine(AppPaths.RootDirectory, "presets"));
        AppPaths.SamplesDirectory.Should().Be(Path.Combine(AppPaths.RootDirectory, "samples"));
        AppPaths.DataSetsDirectory.Should().Be(Path.Combine(AppPaths.RootDirectory, "datasets"));
        AppPaths.ScriptsDirectory.Should().Be(Path.Combine(AppPaths.RootDirectory, "scripts"));

        AppPaths.LocalDataDirectory.Should().EndWith("FileFlowStudio");
        AppPaths.CheckpointsDirectory.Should().Be(Path.Combine(AppPaths.LocalDataDirectory, "checkpoints"));
        AppPaths.ModelsDirectory.Should().Be(Path.Combine(AppPaths.LocalDataDirectory, "models"));
        AppPaths.PluginsDirectory.Should().Be(Path.Combine(AppPaths.LocalDataDirectory, "plugins"));
        AppPaths.LogsDirectory.Should().Be(Path.Combine(AppPaths.LocalDataDirectory, "logs"));

        AppPaths.UserPreferencesFile.Should().Be(Path.Combine(AppPaths.ConfigDirectory, "user_preferences.json"));
        AppPaths.ExternalToolsFile.Should().Be(Path.Combine(AppPaths.ConfigDirectory, "external_tools.json"));
        AppPaths.CustomThemesFile.Should().Be(Path.Combine(AppPaths.ThemesDirectory, "custom_themes.json"));
        AppPaths.RenamerPresetsFile.Should().Be(Path.Combine(AppPaths.PresetsDirectory, "renamer_presets.json"));
        AppPaths.MediaPresetsFile.Should().Be(Path.Combine(AppPaths.PresetsDirectory, "media_presets.json"));
        AppPaths.RegexLibraryFile.Should().Be(Path.Combine(AppPaths.PresetsDirectory, "regex_library.json"));
        AppPaths.RenamerSamplesFile.Should().Be(Path.Combine(AppPaths.SamplesDirectory, "renamer_samples.json"));
        AppPaths.DataSetsFile.Should().Be(Path.Combine(AppPaths.DataSetsDirectory, "synthetic_datasets.json"));
        AppPaths.CrashLogFile.Should().Be(Path.Combine(AppPaths.LogsDirectory, "crash.log"));
    }

    [Fact]
    public void EnsureDirectories_ShouldCreateAllSubdirectoriesWithoutExceptions()
    {
        // Act
        var act = () => AppPaths.EnsureDirectories();

        // Assert
        act.Should().NotThrow();
        Directory.Exists(AppPaths.RootDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.ConfigDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.ThemesDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.PresetsDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.SamplesDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.DataSetsDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.ScriptsDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.LocalDataDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.CheckpointsDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.ModelsDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.PluginsDirectory).Should().BeTrue();
        Directory.Exists(AppPaths.LogsDirectory).Should().BeTrue();
    }

    [Fact]
    public void SetCustomDataDirectory_ShouldRedirectAllPathsCorrectly()
    {
        // Arrange
        string tempCustomDir = Path.Combine(Path.GetTempPath(), "FileFlowStudio_Custom_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Act
            AppPaths.SetCustomDataDirectory(tempCustomDir);

            // Assert
            AppPaths.IsPortableMode.Should().BeTrue();
            AppPaths.RootDirectory.Should().Be(tempCustomDir);
            AppPaths.LocalDataDirectory.Should().Be(tempCustomDir);
            AppPaths.ConfigDirectory.Should().Be(Path.Combine(tempCustomDir, "config"));
            AppPaths.CheckpointsDirectory.Should().Be(Path.Combine(tempCustomDir, "checkpoints"));
            AppPaths.ModelsDirectory.Should().Be(Path.Combine(tempCustomDir, "models"));
            AppPaths.DataSetsDirectory.Should().Be(Path.Combine(tempCustomDir, "datasets"));
            AppPaths.UserPreferencesFile.Should().Be(Path.Combine(tempCustomDir, "config", "user_preferences.json"));

            AppPaths.EnsureDirectories();
            Directory.Exists(Path.Combine(tempCustomDir, "config")).Should().BeTrue();
            Directory.Exists(Path.Combine(tempCustomDir, "checkpoints")).Should().BeTrue();
            Directory.Exists(Path.Combine(tempCustomDir, "models")).Should().BeTrue();
        }
        finally
        {
            AppPaths.SetCustomDataDirectory(null);
            if (Directory.Exists(tempCustomDir))
            {
                try { Directory.Delete(tempCustomDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void ResolveApplicationPath_ShouldHandleAbsoluteAndRelativePaths()
    {
        // Act & Assert
        AppPaths.ResolveApplicationPath("").Should().BeEmpty();
        AppPaths.ResolveApplicationPath(@"C:\Windows\notepad.exe").Should().Be(@"C:\Windows\notepad.exe");

        string relative = Path.Combine("tools", "ffmpeg.exe");
        string resolved = AppPaths.ResolveApplicationPath(relative);
        resolved.Should().EndWith(relative);
        Path.IsPathRooted(resolved).Should().BeTrue();
    }
}
