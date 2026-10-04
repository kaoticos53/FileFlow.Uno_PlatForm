using Microsoft.UI.Xaml;
#if WINDOWS
using System.IO;
using System.Runtime.InteropServices;
#endif

namespace FileFlow.App.Uno.Platform;

/// <summary>
/// La DPI awareness EFECTIVA del proceso del host, medida en runtime, y un rastro opcional de la rueda.
///
/// <para><b>Por qué existe</b>: el host arrancaba DPI-<b>UNAWARE</b> porque <c>app.manifest</c> no declaraba
/// la DPI awareness (medido: <c>[dpi] dpi=UNAWARE, escala=1,00</c> con la pantalla al 125%). Un proceso
/// unaware recibe la ENTRADA virtualizada por Windows, y con ella el hit-test de la rueda eligió mal el
/// destino (un panel se desplazaba con el puntero fuera de él). No era un bug de WinUI: era el manifiesto. La
/// corrección vive en <c>app.manifest</c> (PerMonitorV2) y esta sonda es su medida: en una pantalla escalada
/// <c>RasterizationScale</c> debe ser la escala real (p. ej. 1,25), no 1,00.</para>
///
/// <para>Con <c>FILEFLOW_WHEEL_TRACE=1</c> adjunta al árbol un rastro del <c>PointerWheelChanged</c> real:
/// revela con qué coordenadas y qué origen llega la rueda, que es lo que distingue «no llega» de «llega y se
/// enruta mal». Sin la variable no se engancha nada.</para>
/// </summary>
internal static class DpiDiagnostics
{
    /// <summary>La DPI awareness efectiva del hilo de UI, en palabras. Nunca miente como el shell: PowerShell
    /// es DPI-unaware y Windows le virtualiza el DPI, por eso una medida desde fuera da 96 en una pantalla al
    /// 125%.</summary>
    internal static string Awareness
    {
        get
        {
#if WINDOWS
            try
            {
                // Los DPI_AWARENESS_CONTEXT predefinidos son identificadores negativos; la V2 sólo se
                // distingue comparando el contexto, no por GetAwarenessFromDpiAwarenessContext (que colapsa
                // V1 y V2 en PER_MONITOR).
                nint context = GetThreadDpiAwarenessContext();
                long raw = context.ToInt64();
                if (raw is -1 or -2 or -3 or -4 or -5)
                {
                    return raw switch
                    {
                        -1 => "UNAWARE",
                        -2 => "SYSTEM_AWARE",
                        -3 => "PER_MONITOR",
                        -4 => "PER_MONITOR_V2",
                        _ => "UNAWARE_GDISCALED",
                    };
                }

                return GetAwarenessFromDpiAwarenessContext(context) switch
                {
                    0 => "UNAWARE",
                    1 => "SYSTEM_AWARE",
                    2 => "PER_MONITOR",
                    _ => "ctx=" + raw,
                };
            }
            catch
            {
                return "?";
            }
#else
            return "n/a";
#endif
        }
    }

    /// <summary>La escala de rasterización del árbol (escala real de la pantalla en un host DPI-aware).</summary>
    internal static double ScaleOf(FrameworkElement? root) => root?.XamlRoot?.RasterizationScale ?? 0.0;

    /// <summary>Engancha el rastro de la rueda si <c>FILEFLOW_WHEEL_TRACE=1</c>. No altera la rueda.</summary>
    internal static void AttachTrace(Window window)
    {
#if WINDOWS
        if (!string.Equals(System.Environment.GetEnvironmentVariable("FILEFLOW_WHEEL_TRACE"), "1",
                System.StringComparison.Ordinal)
            || window.Content is not FrameworkElement root)
        {
            return;
        }

        root.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
            {
                try
                {
                    var position = e.GetCurrentPoint(root).Position;
                    File.AppendAllText(
                        Path.Combine(AppContext.BaseDirectory, "wheel-trace.txt"),
                        $"XAML wheel escala={ScaleOf(root):F2} dpi={Awareness} pos=({position.X:F0},{position.Y:F0}) "
                        + $"handled={e.Handled} src={e.OriginalSource?.GetType().Name}"
                        + System.Environment.NewLine);
                }
                catch
                {
                    // El rastro no debe alterar la rueda.
                }
            }),
            handledEventsToo: true);
#endif
    }

#if WINDOWS
    [DllImport("user32.dll")]
    private static extern nint GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(nint value);
#endif
}
