using System.IO;
using FileFlow.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace FileFlow.Tests.Unit.App;
/// <summary>
/// La guardia de la <b>MEDICIÓN</b> de los paneles de nodo (hito 258): el sondeo de los diálogos tiene su modo
/// propio (<c>--selfcheck-dialogs</c>), fuera del recorrido del lienzo, y lee su veredicto del VALOR que queda
/// escrito en el parámetro del nodo —el dato que el nodo ejecuta—, no del texto que el modal enseña.
///
/// <para><b>Qué protege</b>: que el modo exista y lo arranque la línea de comandos (con su envoltorio en
/// <c>run-uno.ps1</c>), que su veredicto quede en su fichero, que pulse por el MISMO canal que un lector de
/// pantalla (el peer de automatización del control) y que lea el parámetro: un diálogo que se abre y no escribe
/// nada cae aquí igual que un botón sin efecto.</para>
/// </summary>
public class UnoNodeDialogProbeGuardTests
{
    private const string WindowService = "FileFlow.App.Uno/Platform/UnoWindowService.cs";
    private const string SelfCheckCode = "FileFlow.App.Uno/SelfCheckDialogs.cs";
    private const string AppCode = "FileFlow.App.Uno/App.xaml.cs";

    private static string Code(string relativePath) => SourceText.CodeWithoutComments(relativePath);

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(TestRepositoryLocator.RepositoryRoot(), relativePath));

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. La medición: modo propio, por el canal del usuario y leyendo el valor escrito
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheProbe_ShouldHaveItsOwnMode_AndReadTheWrittenValue()
    {
        Code(AppCode).Should().Contain("\"--selfcheck-dialogs\"",
            "el sondeo de los paneles de nodo tiene que tener su propio modo: abre modales sobre la misma raíz "
            + "y escribe en el nodo inspeccionado");
        Code(AppCode).Should().Contain("SelfCheckDialogs.Run(");

        string selfCheck = Code(SelfCheckCode);
        selfCheck.Should().Contain("public static int Run(Window window, DispatcherQueue dispatcher)");
        selfCheck.Should().Contain("selfcheck-dialogs-report.txt",
            "el veredicto tiene que quedar en su fichero, como el de las otras sondas");

        // El modo NO puede llamarse desde el recorrido del lienzo: cada modo vive en su archivo (hito 276) y
        // el del lienzo no lo nombra, que es la misma afirmación que antes medía el censo de menciones.
        SourceText.CodeWithoutComments("FileFlow.App.Uno/SelfCheckCanvas.cs").Should().NotContain("SelfCheckDialogs",
            "el sondeo de los paneles de nodo se arranca desde la línea de comandos, no desde el recorrido del lienzo");

        // Pulsa por el MISMO canal que un lector de pantalla (el peer de automatización del control) y lee su
        // veredicto del PARÁMETRO del nodo: el valor que el nodo ejecuta, no el texto del diálogo.
        Code(WindowService).Should().Contain("FrameworkElementAutomationPeer.CreatePeerForElement");
        selfCheck.Should().Contain("UnoWindowService.Press(");
        selfCheck.Should().Contain("ParamEditor_");
        selfCheck.Should().Contain("ParamVariable_");
        // El veredicto es el VALOR del parámetro del nodo —el dato que el nodo ejecuta—, no el texto que el
        // modal enseña: un diálogo que se abre y no escribe nada cae aquí igual que un botón sin efecto.
        selfCheck.Should().Contain("exampleParam?.Value?.ToString()");
        selfCheck.Should().Contain("longParam?.Value?.ToString()");

        // El envoltorio de la línea de comandos tiene que enrutar el modo nuevo (y esperar el veredicto).
        Read("run-uno.ps1").Should().Contain("\"--selfcheck-dialogs\"");
        Read("run-uno-fast.ps1").Should().Contain("\"--selfcheck-dialogs\"");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. El refresco del editor del renombrador (hito 327): la medición y su cura
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheProbe_ShouldMeasureTheRenamerStepRefresh_AndTheBodyMustNotDetachOnUnloaded()
    {
        // La MEDICIÓN tiene que seguir en el sondeo: el estudio se abre por su botón «🏷️», se selecciona
        // OTRO paso en la lista por el CONTROL y se leen los dos eslabones —el paso llegando al view model
        // y el editor lateral repuplándose— más la traza del ciclo de vida que reveló la causa.
        string selfCheck = Code(SelfCheckCode);
        selfCheck.Should().Contain("ActiveAdvancedRenamer");
        selfCheck.Should().Contain("StepsList.SelectedItem");
        selfCheck.Should().Contain("ciclo del cuerpo");
        selfCheck.Should().Contain("EditorRefreshCount");

        // La CURA: el cuerpo NO puede desuscribirse del view model en `Unloaded`. WinUI dispara ese evento
        // CON EL MODAL EN PANTALLA (medido: «ctor+loaded+unloaded+unloaded», sin carga posterior), y esa
        // desuscripción dejaba el editor enseñando el PRIMERO de los pasos para el resto de la sesión.
        string body = Code("FileFlow.App.Uno/Controls/AdvancedRenamerBody.xaml.cs");
        body.Should().NotContain("Unloaded += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged",
            "la suscripción del editor no se ata a la descarga: con el modal en pantalla llegan Unloaded "
            + "sin Loaded posterior y el panel quedaría mudo ante cualquier cambio de paso");
        body.Should().Contain("Loaded += (_, _)",
            "y la carga vuelve a reatar la suscripción y a repoblar el editor por si algo la hubiese desatado");
    }
}
