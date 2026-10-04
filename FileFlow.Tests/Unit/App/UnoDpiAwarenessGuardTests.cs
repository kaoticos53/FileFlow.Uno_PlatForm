using System.IO;
using FileFlow.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace FileFlow.Tests.Unit.App;

/// <summary>
/// Guardia de la DPI awareness del host. El host arrancaba DPI-<b>UNAWARE</b> (medido: <c>dpi=UNAWARE,
/// escala=1,00</c> con la pantalla al 125%) porque <c>app.manifest</c> no declaraba la awareness; un proceso
/// unaware recibe la entrada virtualizada y con ella el hit-test de la rueda se rompe a escala != 100%. Esta
/// guardia ata la declaración que lo corrige y la medida que lo vigila.
/// </summary>
public class UnoDpiAwarenessGuardTests
{
    private const string Manifest = "FileFlow.App.Uno/app.manifest";
    private const string SelfCheckCanvasCode = "FileFlow.App.Uno/SelfCheckCanvas.cs";
    private const string DpiDiagnosticsCode = "FileFlow.App.Uno/Platform/DpiDiagnostics.cs";
    private const string MainWindowCode = "FileFlow.App.Uno/MainWindow.xaml.cs";

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(TestRepositoryLocator.RepositoryRoot(), relativePath));

    [Fact]
    public void AppManifest_ShouldDeclarePerMonitorV2DpiAwareness()
    {
        string manifest = Read(Manifest);

        manifest.Should().Contain("dpiAwareness",
            "sin la declaración el proceso arranca DPI-UNAWARE y Windows le virtualiza la entrada");
        manifest.Should().Contain("PerMonitorV2");
        manifest.Should().Contain("dpiAware");
        manifest.Should().Contain("8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a",
            "la declaración de <dpiAwareness> sólo se honra si Windows 10/11 está declarado como soportado");
    }

    [Fact]
    public void SelfCheck_ShouldReportTheEffectiveDpiAwareness()
    {
        string selfCheck = SourceText.CodeWithoutComments(SelfCheckCanvasCode);

        selfCheck.Should().Contain("DpiDiagnostics.Awareness");
        selfCheck.Should().Contain("DpiDiagnostics.ScaleOf");

        string diagnostics = SourceText.CodeWithoutComments(DpiDiagnosticsCode);
        diagnostics.Should().Contain("GetThreadDpiAwarenessContext");

        SourceText.CodeWithoutComments(MainWindowCode).Should().Contain("DpiDiagnostics.AttachTrace(this)");
    }
}
