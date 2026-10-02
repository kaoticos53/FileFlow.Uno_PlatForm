using System;
using FileFlow.Sdk;
using FileFlow.Sdk.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FileFlow.App.Uno.Controls;

public sealed partial class SplashScreenView : UserControl
{
    private readonly DispatcherTimer _shimmerTimer;
    private bool _shimmerEnabled;
    private LinearGradientBrush? _shimmerBrush;
    private GradientStop? _shimmerStop;
    private double _shimmerOffset;

    public SplashScreenView()
    {
        InitializeComponent();

        TxtVersion.Text = AppVersionInfo.DisplayVersion;
        RefreshLocalizedTexts();
        SetupShimmerBrush();

        _shimmerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _shimmerTimer.Tick += (_, _) => AdvanceShimmer();
    }

    public void RefreshLocalizedTexts()
    {
        var loc = LocalizationManager.Instance;
        TxtEngine.Text = loc.GetString("Splash_InitializingEngine", "Inicializando Motor de Flujo DAG...");
        TxtNodesBadge.Text = loc.GetString("Splash_LoadingNodes", "Cargando nodos...");
        TxtStatus.Text = loc.GetString("Splash_StatusServices", "Construyendo el contenedor de servicios...");
        TxtFooter.Text = loc.GetString("Splash_Footer", "© 2026 RGLara • Cross-Platform Engine");
    }

    public void UpdateStatus(string message, double progress)
    {
        TxtStatus.Text = message;
        PbProgress.Value = Math.Clamp(progress, 0, 100);
        TxtPercentage.Text = $"{(int)PbProgress.Value}%";
    }

    public void SetNodeCount(int count)
    {
        TxtNodesBadge.Text = LocalizationManager.Instance.GetFormattedString(
            "Splash_NodesBadge", $"{count} nodos DAG", count);
    }

    public void StartShimmer()
    {
        _shimmerEnabled = true;
        _shimmerTimer.Start();
    }

    public void StopShimmer()
    {
        _shimmerEnabled = false;
        _shimmerTimer.Stop();
    }

    public void ApplyTheme(bool isDark)
    {
        RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
        SetupShimmerBrush();
    }

    private void SetupShimmerBrush()
    {
        try
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0.5),
                EndPoint = new Windows.Foundation.Point(1, 0.5)
            };

            var baseColor = (Application.Current.Resources["CanvasAccentPrimaryBrush"] as SolidColorBrush)?.Color
                ?? Windows.UI.Color.FromArgb(255, 99, 102, 241);
            var glowColor = (Application.Current.Resources["CanvasAccentGlowBrush"] as SolidColorBrush)?.Color
                ?? Windows.UI.Color.FromArgb(255, 129, 140, 248);

            brush.GradientStops.Add(new GradientStop { Color = baseColor, Offset = 0.0 });
            _shimmerStop = new GradientStop { Color = glowColor, Offset = 0.5 };
            brush.GradientStops.Add(_shimmerStop);
            brush.GradientStops.Add(new GradientStop { Color = baseColor, Offset = 1.0 });

            _shimmerBrush = brush;
            PbProgress.Foreground = brush;
        }
        catch
        {
            // Conserva el color estático si los recursos aún no están cargados
        }
    }

    /// <summary>
    /// Avanza una posición la animación de barrido. Es público para que las pruebas de contrato
    /// puedan ejercitar el paso que ejecuta el temporizador sin esperar tiempo real.
    /// </summary>
    public void AdvanceShimmer()
    {
        if (!_shimmerEnabled || _shimmerStop is null)
        {
            return;
        }

        _shimmerOffset = (_shimmerOffset + 0.03) % 1.0;
        _shimmerStop.Offset = _shimmerOffset;
    }
}
