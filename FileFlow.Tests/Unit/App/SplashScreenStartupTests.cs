using System;
using System.IO;
using System.Xml.Linq;
using FileFlow.App.Services;
using FileFlow.Sdk.Localization;
using FileFlow.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace FileFlow.Tests.Unit.App;

/// <summary>
/// Guardias de la <b>pantalla de carga inicial (Splash Screen)</b> del host Uno.
///
/// Verifica que:
/// <list type="bullet">
///   <item>Todas las claves de localización de la splash existen en ES y EN con texto significativo.</item>
///   <item>La etapa <see cref="StartupPhase.Splash"/> está declarada con descripción legible.</item>
///   <item>El host Uno (<c>App.xaml.cs</c>) integra la pantalla de carga durante el arranque.</item>
///   <item>La vista reutilizable (<c>SplashScreenView.xaml</c>) declara todos los controles y bindings necesarios.</item>
///   <item>La ventana independiente (<c>SplashScreenWindow.xaml</c>) y la capa superpuesta (<c>MainWindow.xaml</c>) integran la vista.</item>
/// </list>
/// </summary>
public class SplashScreenStartupTests
{
    private static string RepoRoot => TestRepositoryLocator.RepositoryRoot();

    private static readonly string[] RequiredSplashKeys =
    [
        "Splash_InitializingEngine",
        "Splash_LoadingNodes",
        "Splash_NodesBadge",
        "Splash_StatusServices",
        "Splash_StatusPreferences",
        "Splash_StatusTheme",
        "Splash_StatusPlugins",
        "Splash_StatusInterface",
        "Splash_StatusReady",
        "Splash_Footer",
    ];

    [Theory]
    [InlineData("Strings.resx")]
    [InlineData("Strings.es.resx")]
    public void EverySplashKey_ShouldExistInBothDictionaries_WithMeaningfulContent(string resxName)
    {
        string resxPath = Path.Combine(RepoRoot, "FileFlow.App.Core", "Resources", resxName);
        File.Exists(resxPath).Should().BeTrue($"el archivo {resxName} debe existir");

        var doc = XDocument.Load(resxPath);
        var entries = doc.Root?.Elements("data")
            .ToDictionary(
                e => e.Attribute("name")?.Value ?? string.Empty,
                e => e.Element("value")?.Value ?? string.Empty)
            ?? [];

        foreach (string key in RequiredSplashKeys)
        {
            entries.Should().ContainKey(key, $"la clave {key} debe estar presente en {resxName}");
            entries[key].Should().NotBeNullOrWhiteSpace($"la clave {key} debe tener contenido no vacío en {resxName}");
        }
    }

    [Fact]
    public void SplashPhase_ShouldHaveALegibleName_InSpanishAndEnglish()
    {
        string key = StartupPhaseDescriptions.Key(StartupPhase.Splash);
        key.Should().Be("Startup_Phase_Splash");

        string fallback = StartupPhaseDescriptions.Fallback(StartupPhase.Splash);
        fallback.Should().NotBeNullOrWhiteSpace();

        string desc = StartupPhaseDescriptions.Describe(StartupPhase.Splash);
        desc.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void App_ShouldWireSplashScreen_InUnoStartup()
    {
        string appPath = Path.Combine(RepoRoot, "FileFlow.App.Uno", "App.xaml.cs");
        string code = SourceText.CodeWithoutComments(appPath);

        code.Should().Contain("SplashScreenWindow", "el arranque debe referenciar la ventana de splash en Windows");
        code.Should().Contain("ShowSplashOverlay", "el arranque debe soportar la capa overlay como degradación en Web/fallback");
        code.Should().Contain("Splash_StatusServices", "el arranque debe comunicar la etapa de servicios");
        code.Should().Contain("Splash_StatusReady", "el arranque debe comunicar cuando está listo");
    }

    [Fact]
    public void SplashScreenView_ShouldDeclareAllRequiredVisualElements()
    {
        string viewPath = Path.Combine(RepoRoot, "FileFlow.App.Uno", "Controls", "SplashScreenView.xaml");
        File.Exists(viewPath).Should().BeTrue("SplashScreenView.xaml debe existir");

        string xaml = File.ReadAllText(viewPath);
        xaml.Should().Contain("x:Name=\"TxtVersion\"", "debe declarar etiqueta para la versión");
        xaml.Should().Contain("x:Name=\"TxtEngine\"", "debe declarar etiqueta para el estado del motor");
        xaml.Should().Contain("x:Name=\"TxtNodesBadge\"", "debe declarar insignia para el conteo de nodos");
        xaml.Should().Contain("x:Name=\"TxtStatus\"", "debe declarar etiqueta de estado");
        xaml.Should().Contain("x:Name=\"TxtPercentage\"", "debe declarar porcentaje");
        xaml.Should().Contain("x:Name=\"PbProgress\"", "debe declarar barra de progreso");
        xaml.Should().Contain("x:Name=\"TxtFooter\"", "debe declarar pie de copyright");
    }

    [Fact]
    public void SplashScreenWindow_ShouldHostSplashScreenView()
    {
        string windowPath = Path.Combine(RepoRoot, "FileFlow.App.Uno", "SplashScreenWindow.xaml");
        File.Exists(windowPath).Should().BeTrue("SplashScreenWindow.xaml debe existir");

        string xaml = File.ReadAllText(windowPath);
        xaml.Should().Contain("controls:SplashScreenView", "la ventana debe alojar SplashScreenView");
    }

    [Fact]
    public void MainWindow_ShouldContainSplashOverlay_ForWebAndFallback()
    {
        string mainWindowPath = Path.Combine(RepoRoot, "FileFlow.App.Uno", "MainWindow.xaml");
        string xaml = File.ReadAllText(mainWindowPath);

        xaml.Should().Contain("x:Name=\"SplashOverlayRoot\"", "MainWindow debe contener el contenedor de la capa splash");
        xaml.Should().Contain("x:Name=\"SplashOverlay\"", "MainWindow debe contener la vista SplashScreenView");
    }
}
