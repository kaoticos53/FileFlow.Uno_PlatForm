using System.IO;
using FileFlow.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace FileFlow.Tests.Unit.App;

/// <summary>
/// Guardia de la consola de logs del host Uno: verifica que el panel inferior exista, esté integrado
/// en el marco con su divisor redimensionable, adaptador expandible con soporte JSON y exportador de logs.
/// </summary>
public class UnoLogPanelGuardTests
{
    private const string PanelXaml = "FileFlow.App.Uno/Controls/LogPanel.xaml";
    private const string PanelCode = "FileFlow.App.Uno/Controls/LogPanel.xaml.cs";
    private const string ItemVmCode = "FileFlow.App.Uno/Controls/LogItemViewModel.cs";
    private const string SplitterCode = "FileFlow.App.Uno/Controls/PanelSplitter.cs";
    private const string MainWindowXaml = "FileFlow.App.Uno/MainWindow.xaml";
    private const string MainWindowCode = "FileFlow.App.Uno/MainWindow.xaml.cs";
    private const string AppCode = "FileFlow.App.Uno/App.xaml.cs";

    private static string Code(string relativePath) => SourceText.CodeWithoutComments(relativePath);
    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(TestRepositoryLocator.RepositoryRoot(), relativePath));

    [Fact]
    public void LogPanel_ShouldDeclareAllControlsAndFeatures()
    {
        string xaml = Read(PanelXaml);
        string code = Code(PanelCode);

        // Controles de barra de herramientas declarados en XAML
        xaml.Should().Contain("BtnFilterAll");
        xaml.Should().Contain("BtnFilterErrors");
        xaml.Should().Contain("BtnFilterWarnings");
        xaml.Should().Contain("BtnFilterInfo");
        xaml.Should().Contain("BtnFilterDebug");
        xaml.Should().Contain("SearchBox");
        xaml.Should().Contain("BtnLiveToggle");
        xaml.Should().Contain("BtnExport");
        xaml.Should().Contain("BtnClear");
        xaml.Should().Contain("BtnCollapse");
        xaml.Should().Contain("LogListView");

        // Codebehind gestiona filtrado, colapso y sincronización
        code.Should().Contain("SetFilter(\"All\")");
        code.Should().Contain("SetFilter(\"Errors\")");
        code.Should().Contain("SetFilter(\"Warnings\")");
        code.Should().Contain("SetFilter(\"Info\")");
        code.Should().Contain("SetFilter(\"Debug\")");
        code.Should().Contain("CollapseRequested");
        code.Should().Contain("ToggleExpanded");
        code.Should().Contain("ContentDialogWheelScroller.EnableScrollSurface(LogListView)",
            "la consola debe recuperar la rueda incluso cuando la lista o sus elementos consumen el evento");
    }

    [Fact]
    public void LogItemViewModel_ShouldProvideExpandableAdapterWithJsonFormatting()
    {
        string code = Code(ItemVmCode);

        code.Should().Contain("INotifyPropertyChanged");
        code.Should().Contain("bool IsExpanded");
        code.Should().Contain("ToggleExpanded()");
        code.Should().Contain("CopyJson()");
        code.Should().Contain("CopyFullLine()");
        code.Should().Contain("CopyMessage()");
        code.Should().Contain("DisplayDetails");
        code.Should().Contain("FormattedShortItemId");
    }

    [Fact]
    public void PanelSplitter_ShouldSupportRowResizing()
    {
        string code = Code(SplitterCode);

        code.Should().Contain("AttachRow(RowDefinition row");
        code.Should().Contain("InputSystemCursorShape.SizeNorthSouth");
        code.Should().Contain("DragRowBy");
        code.Should().Contain("RememberedHeight");
    }

    [Fact]
    public void MainWindow_ShouldIntegrateLogSplitterAndConsole()
    {
        string xaml = Read(MainWindowXaml);
        string code = Code(MainWindowCode);

        xaml.Should().Contain("<controls:PanelSplitter x:Name=\"LogSplitter\"");
        xaml.Should().Contain("<controls:LogPanel x:Name=\"LogsConsole\"");
        xaml.Should().Contain("BtnToggleLogs");

        code.Should().Contain("LogSplitter.AttachRow(LogRow");
        code.Should().Contain("ApplyLogPanelVisibility");
        code.Should().Contain("LogsConsole.Vm = mainVm.LogConsole");
    }

    [Fact]
    public void App_ShouldInstallLogExporterBridge()
    {
        string code = Code(AppCode);

        code.Should().Contain("HostUi.SetLogExporter");
        code.Should().Contain("ShowSaveFileDialogAsync");
        code.Should().Contain("ExportLogsAsync");
    }
}
