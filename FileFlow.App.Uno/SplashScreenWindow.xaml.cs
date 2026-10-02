using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace FileFlow.App.Uno;

/// <summary>
/// Ventana flotante de carga inicial (Splash Screen) para el host de escritorio.
/// </summary>
public sealed partial class SplashScreenWindow : Window
{
    public SplashScreenWindow()
    {
        InitializeComponent();

#if WINDOWS
        ConfigureWindowsPlatformWindow();
#endif
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("User32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    private void ConfigureWindowsPlatformWindow()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            if (appWindow is not null)
            {
                if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.IsResizable = false;
                    presenter.IsMaximizable = false;
                    presenter.IsMinimizable = false;
                    presenter.SetBorderAndTitleBar(false, false);
                    presenter.IsAlwaysOnTop = true;
                }

                uint dpi = GetDpiForWindow(hWnd);
                double scale = dpi > 0 ? (dpi / 96.0) : 1.0;

                int physWidth = (int)Math.Round(540 * scale);
                int physHeight = (int)Math.Round(350 * scale);
                appWindow.Resize(new Windows.Graphics.SizeInt32(physWidth, physHeight));

                var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Primary);
                if (displayArea is not null)
                {
                    int x = (displayArea.WorkArea.Width - physWidth) / 2 + displayArea.WorkArea.X;
                    int y = (displayArea.WorkArea.Height - physHeight) / 2 + displayArea.WorkArea.Y;
                    appWindow.Move(new Windows.Graphics.PointInt32(x, y));
                }
            }
        }
        catch
        {
            // Fallback seguro si AppWindow no está disponible en este entorno
        }
    }
#endif

    public void ApplyTheme(bool isDark)
    {
        if (RootGrid is not null)
        {
            RootGrid.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
        }

        SplashView.ApplyTheme(isDark);
    }

    public void UpdateStatus(string message, double progress)
    {
        SplashView.UpdateStatus(message, progress);
    }

    public void SetNodeCount(int count)
    {
        SplashView.SetNodeCount(count);
    }

    public void StartShimmer()
    {
        SplashView.StartShimmer();
    }

    public async Task CloseWithFadeAsync()
    {
        SplashView.StopShimmer();
        if (RootGrid is not null)
        {
            for (double op = 1.0; op > 0.05; op -= 0.15)
            {
                RootGrid.Opacity = op;
                await Task.Delay(16);
            }
        }

        Close();
    }
}
