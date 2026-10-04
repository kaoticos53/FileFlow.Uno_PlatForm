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

    /// <summary>
    /// El stick-to-bottom de la consola. Antes <c>ScrollToBottomIfLive</c> perseguía el final de forma
    /// INCONDICIONAL: cada lote de registros arrastraba la vista aunque el usuario estuviera leyendo algo
    /// de arriba, y con el buffer lleno la lista se reconstruía entera (Clear + re-add) — juntos, el
    /// «se mueve el panel de logs cuando el cursor está sobre el catálogo».
    ///
    /// <para><b>Qué vigila</b>: que sólo se siga la cola si YA se estaba al fondo, que la sincronía con el
    /// buffer sea incremental en vez de reconstruir por lote, y que lo que queda por debajo se ofrezca
    /// como píldora —una promesa de «volver al final» que el usuario decide pulsar, no que la lista
    /// decida por él.</para>
    /// </summary>
    [Fact]
    public void LogPanel_ShouldFollowTheTailOnlyWhenAlreadyAtTheBottom()
    {
        string code = Code(PanelCode);

        code.Should().Contain("if (_vm?.IsLiveMode == true && _followTail && _displayedItems.Count > 0)",
            "la vuelta al fondo exige el seguimiento ACTIVO: mover la lista porque llegaron registros, " +
            "sin preguntar dónde está el usuario, es el movimiento solo que reportaba el usuario");

        code.Should().Contain("bool following = _followTail && _vm.IsLiveMode;",
            "la decisión se toma ANTES de tocar la lista: al añadir cambia el extent y el ViewChanged " +
            "puede alterar el seguimiento justo cuando se le pregunta");

        code.Should().Contain("SyncDisplayedItems()",
            "los lotes se sincronizan incrementalmente contra el buffer: reconstruir los 2000 registros " +
            "en cada lote era el tirón de la vista");

        code.Should().NotContain("countDiff <= 50",
            "el umbral de 50 que decide reconstruir ya no existe: por encima de él se añadía entero " +
            "y la lista saltaba");

        string xaml = Read(PanelXaml);
        xaml.Should().Contain("AutomationProperties.AutomationId=\"LogNewRecordsPill\"",
            "lo que queda por debajo se ofrece como píldora con su ancla, para que el usuario vuelva " +
            "al final por una decisión propia y el observador externo pueda verla");
    }
}
