using System;
using System.IO;
using System.Linq;
using FileFlow.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace FileFlow.Tests.Unit.App;

/// <summary>
/// Guardia del contrato de la RUEDA DEL RATÓN del host Uno (hito 319): la resolución del destino por el
/// PUNTO del puntero, el enganche único por panel y la sonda que lo mide.
///
/// <para><b>El defecto que vigila</b>: la rueda se elegía por el <c>OriginalSource</c> —el elemento exacto
/// bajo el cursor—, así que funcionaba «en unas zonas y en otras no», y el inspector tenía tres manejadores
/// a la vez que se pisaban (el doble movimiento). El arreglo resuelve el destino por el punto. Esta guardia
/// fija las tres piezas que lo sostienen: que el helper siga resolviendo por punto, que cada panel tenga UNA
/// sola superficie, y que exista una medida real —interna en el sondeo, física en el observador externo—,
/// porque un contrato sin sonda se rompe sin que nadie se entere.</para>
///
/// <para><b>Por qué guarda la fuente y no el runtime</b>: los paneles son WinUI y no se materializan en la
/// sesión de pruebas (la lección del lienzo, hito 232). Lo que el sondeo AFIRMA en la app viva lo escribe su
/// informe; aquí se fija que la vía de medida exista y no se borre.</para>
/// </summary>
public class UnoWheelWiringGuardTests
{
    private const string HelperPath = "FileFlow.App.Uno/Platform/ContentDialogWheelScroller.cs";
    private const string LogPath = "FileFlow.App.Uno/Controls/LogPanel.xaml.cs";
    private const string LogXamlPath = "FileFlow.App.Uno/Controls/LogPanel.xaml";
    private const string ToolboxPath = "FileFlow.App.Uno/Controls/NodeToolboxPanel.xaml.cs";
    private const string ToolboxXamlPath = "FileFlow.App.Uno/Controls/NodeToolboxPanel.xaml";
    private const string InspectorPath = "FileFlow.App.Uno/Controls/NodeInspectorPanel.xaml.cs";
    private const string InspectorProbesPath = "FileFlow.App.Uno/Controls/NodeInspectorPanel.Probes.cs";
    private const string WheelProbePath = "FileFlow.App.Uno/SelfCheckWheel.cs";
    private const string CanvasPath = "FileFlow.App.Uno/SelfCheckCanvas.cs";
    private const string SettingsPath = "FileFlow.App.Uno/Controls/SettingsPanel.xaml.cs";
    private const string DrawerPath = "FileFlow.App.Uno/Controls/MainMenuDrawer.xaml.cs";
    private const string WindowServicePath = "FileFlow.App.Uno/Platform/UnoWindowService.cs";
    private const string DialogServicePath = "FileFlow.App.Uno/Platform/UnoDialogService.cs";
    private const string CanvasNavPath = "FileFlow.App.Uno/Controls/EditorCanvasControl.Navigation.cs";

    private static string Code(string path) => SourceText.CodeWithoutComments(path);

    /// <summary>
    /// El helper resuelve el destino por el punto del puntero y expone el MISMO camino al sondeo: sin
    /// <c>ResolveTargetAtPoint</c>/<c>ApplyWheelAtPoint</c>, la sonda tendría que medir por otra vía y
    /// certificaría un comportamiento que el evento real no recorre.
    /// </summary>
    [Fact]
    public void TheHelper_ShouldResolveTheTargetByPoint_AndExposeTheSamePathToTheProbe()
    {
        string code = Code(HelperPath);

        code.Should().Contain("FindScrollViewerUnderPoint(surface, point)",
            "el destino sale del PUNTO, no del OriginalSource: el origen cambia con el elemento bajo el cursor");

        code.Should().Contain("internal static bool ApplyWheelAtPoint(UIElement surface, Point point, int delta, bool horizontal)",
            "la muesca se aplica por un método que el sondeo puede llamar con su propio punto (el mismo que decide el evento)");

        code.Should().Contain("internal static ScrollViewer? ResolveTargetAtPoint(UIElement surface, Point point)",
            "y el destino se resuelve por el mismo punto para poder medir qué viewer se elegiría");

        code.Should().Contain("if (e.Handled)",
            "el manejador cede el paso si un hijo ya aplicó la rueda: el doble desplazamiento era la erraticidad");

        code.Should().NotContain("e.OriginalSource",
            "el origen del evento NO puede volver a decidir el destino: era la causa de que la rueda dependiera de la zona");

        code.Should().NotContain("Enable(ContentDialog",
            "la puerta de entrada es única: un `Enable(ContentDialog)` era el manejador ad-hoc para un tipo de superficie");
        code.Should().NotContain("OnDialogPointerWheelChanged",
            "y su manejador paralelo se retiró: tener el motor dos veces era el doble desplazamiento");
    }

    /// <summary>
    /// El contrato ÚNICO cubre también las superficies que no son diálogos: los paneles de la VENTANA (ajustes y
    /// cajón del menú) se enganchan a la misma puerta. Antes cada superficie empujaba el desplazamiento a mano.
    /// </summary>
    [Fact]
    public void EveryWindowScrollSurface_ShouldUseTheSingleContract()
    {
        Code(SettingsPath).Should().Contain("ContentDialogWheelScroller.EnableScrollSurface(this)",
            "Ajustes engancha una sola vez su raíz: sus seis secciones cuelgan de ahí y el destino se resuelve por punto");
        Code(DrawerPath).Should().Contain("ContentDialogWheelScroller.EnableScrollSurface(this)",
            "el cajón del menú engancha su raíz por el mismo contrato");

        Code(SettingsPath).Should().NotContain("PointerWheelChanged",
            "y no queda en Ajustes ningún manejador de rueda ad-hoc por sección");
        Code(DrawerPath).Should().NotContain("PointerWheelChanged",
            "ni en el cajón");
    }

    /// <summary>
    /// TODO modal del host —incluido el DISEÑADOR DE DATASETS, que es un cuerpo de diálogo— pasa por el sobre
    /// central, que es donde se engancha la rueda. La confirmación suelta, que no pasa por ese sobre, engancha a
    /// mano y por eso se fija aquí: es la excepción declarada, no un olvido.
    /// </summary>
    [Fact]
    public void EveryModal_ShouldPassThroughTheSingleWheelHook()
    {
        Code(WindowServicePath).Should().Contain("ContentDialogWheelScroller.EnableScrollSurface(dialog)",
            "el sobre de todo modal (ShowOwnedModalAsync) es donde se engancha la rueda: los cuerpos de diálogo —diseñador de datasets incluido— no la enganchan por su cuenta");

        Code(DialogServicePath).Should().Contain("ContentDialogWheelScroller.EnableScrollSurface(dialog)",
            "la confirmación suelta crea su propio ContentDialog fuera de ese sobre y por eso engancha explícitamente");
    }

    /// <summary>
    /// El lienzo es la ÚNICA otra rueda del host y no desplaza: hace ZOOM. Esta prueba barre el código de
    /// producción del host y exige que nadie más toque <c>PointerWheelChanged</c>/<c>MouseWheelDelta</c>: cualquier
    /// superficie nueva que enganche la rueda a mano en vez de por el contrato hace fallar la guardia.
    /// </summary>
    [Fact]
    public void TheCanvasZoom_ShouldBeTheOnlyOtherWheelHandler()
    {
        var offenders = SourceTree.CSharpFiles(TestRepositoryLocator.RepositoryRoot())
            .Where(f => f.File.StartsWith("FileFlow.App.Uno/", StringComparison.Ordinal))
            .Where(f => !f.File.Equals(HelperPath, StringComparison.Ordinal))
            .Where(f => !f.File.Equals(CanvasNavPath, StringComparison.Ordinal))
            .Where(f =>
            {
                string code = SourceText.WithoutComments(f.Source);
                return code.Contains("PointerWheelChanged", StringComparison.Ordinal)
                    || code.Contains("MouseWheelDelta", StringComparison.Ordinal);
            })
            .Select(f => f.File)
            .ToList();

        offenders.Should().BeEmpty(
            "todo el host resuelve la rueda por ContentDialogWheelScroller; la única excepción es el zoom del lienzo "
            + "(EditorCanvasControl.Navigation.cs), y cualquier otro enganche ad-hoc debe fallar aquí");
    }

    /// <summary>Cada panel declara UNA superficie de rueda y su ancla UIA, para el sondeo interno y el externo.</summary>
    [Fact]
    public void EachPanel_ShouldDeclareItsWheelSurface_AndItsUiaAnchor()
    {
        Code(LogPath).Should().Contain("internal UIElement WheelSurfaceForProbe => LogListView",
            "la consola mide la rueda sobre su lista, que es su superficie desplazable");
        Code(LogXamlPath).Should().Contain("AutomationProperties.AutomationId=\"LogScrollSurface\"",
            "y la lista canta su ancla para que el observador externo la alcance por nombre");

        Code(ToolboxPath).Should().Contain("internal UIElement WheelSurfaceForProbe => ToolboxScroll",
            "el catálogo mide la rueda sobre su ScrollViewer completo");
        Code(ToolboxXamlPath).Should().Contain("AutomationProperties.AutomationId=\"ToolboxScrollSurface\"",
            "y su ancla estable");

        Code(InspectorProbesPath).Should().Contain("internal UIElement WheelSurfaceForProbe => this",
            "la ficha mide la rueda sobre su raíz, que es donde tiene el enganche único");
        Code(InspectorPath).Should().Contain("AutomationProperties.SetAutomationId(paramsScroll, \"InspectorParamsScrollSurface\")",
            "y su pestaña de parámetros canta el ancla de su scroll anidado, el que desplaza sus filas");
    }

    /// <summary>
    /// La sonda INTERNA existe, corre el mismo camino y se invoca desde el sondeo del lienzo. Sin esta
    /// medida, la resolución por punto podría romperse y la suite seguiría verde.
    /// </summary>
    [Fact]
    public void TheRuntimeProbe_ShouldMeasureTheWheel_AndBeInvokedByTheCanvasCheck()
    {
        string probe = Code(WheelProbePath);

        probe.Should().Contain("internal static class SelfCheckWheel",
            "la medida de la rueda vive en su propio archivo, junto a su instrumento");
        probe.Should().Contain("ContentDialogWheelScroller.ApplyWheelAtPoint(",
            "y recorre el MISMO camino que el evento real: medir por otro camino certificaría otra cosa");
        probe.Should().Contain("ContentDialogWheelScroller.ResolveTargetAtPoint(",
            "resolviendo el destino por punto, que es lo que el arreglo cambió");

        Code(CanvasPath).Should().Contain("SelfCheckWheel.Check(",
            "el sondeo del lienzo corre la medida de la rueda (una sonda que nadie llama no mide nada)");
    }

    /// <summary>
    /// La sonda EXTERNA inyecta rueda FÍSICA: es la única medida con un ratón de verdad. El sondeo interno
    /// recorre el código de decisión; éste comprueba que el ratón mueve la superficie.
    /// </summary>
    [Fact]
    public void TheExternalProbe_ShouldInjectRealWheelOverTheThreeSurfaces()
    {
        string probe = File.ReadAllText(Path.Combine(
            TestRepositoryLocator.RepositoryRoot(), "docs", "qa", "selfcheck_uia_probe.py"));

        probe.Should().Contain("MOUSEEVENTF_WHEEL",
            "la rueda tiene que ser la de verdad (mouse_event), no una llamada al método interno");
        probe.Should().Contain("u32.SetCursorPos(",
            "el cursor se coloca sobre la superficie antes de girar la rueda");

        foreach (string anchor in new[] { "LogScrollSurface", "ToolboxScrollSurface", "InspectorParamsScrollSurface" })
        {
            probe.Should().Contain(anchor,
                $"el observador externo mide la rueda sobre el ancla '{anchor}'");
        }
    }
}
