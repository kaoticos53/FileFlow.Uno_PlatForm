using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using FileFlow.App.ViewModels;
using FileFlow.App.Uno.Platform;
using FileFlow.Sdk.Localization;
using FileFlow.Sdk.Telemetry;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FileFlow.App.Uno.Controls;

public sealed partial class LogPanel : UserControl
{
    private LogViewModel? _vm;
    private readonly ObservableCollection<LogItemViewModel> _displayedItems = new();
    private bool _isUpdatingFilterButtons;

    /// <summary>El <c>ScrollViewer</c> de dentro de la lista, encontrado al materializarse: de él se lee si la consola está al fondo.</summary>
    private ScrollViewer? _listScroll;

    /// <summary>
    /// ¿La consola sigue la cola? Se decide en CADA lote por la posición ANTERIOR: perseguir el final de
    /// forma incondicional —lo que hacía <c>ScrollToBottomIfLive</c>— era el «se mueve el panel de logs»
    /// que se veía con el cursor en otra parte del host. Sólo si ya estabas al fondo vuelves a estarlo.
    /// </summary>
    private bool _followTail = true;

    /// <summary>Registros que han llegado desde que el usuario dejó el fondo: es lo que cuenta la píldora.</summary>
    private int _unseenCount;

    /// <summary>
    /// El offset y el extent de la vista ANTERIOR. El par es lo que distingue un scroll del USUARIO
    /// (el extent no cambia y el offset baja) de un repliegue del propio buffer (el extent se encoge y
    /// el offset lo sigue): sin el extent, un filtro o una recarga del histórico detendrían el seguimiento
    /// sin que nadie hubiera tocado la rueda.
    /// </summary>
    private double _lastOffset;
    private double _lastExtent;

    public event EventHandler? CollapseRequested;

    public ObservableCollection<LogItemViewModel> DisplayedItems => _displayedItems;

    public LogViewModel? Vm
    {
        get => _vm;
        set
        {
            if (_vm != value)
            {
                if (_vm != null)
                {
                    UnsubscribeFromVm(_vm);
                }

                _vm = value;

                if (_vm != null)
                {
                    SubscribeToVm(_vm);
                    SyncAll();
                }
            }
        }
    }

    public LogPanel()
    {
        InitializeComponent();
        // La rueda NO se engancha: la lista desplaza con el ScrollViewer nativo del ListView, que es el
        // comportamiento de fábrica de WinUI. El host no tiene NINGUNA rueda propia: el motor del hito 319
        // se retiró por errático y el zoom del lienzo por rueda también (hito 324).
        LogListView.ItemsSource = _displayedItems;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalizedTexts();
        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        AttachListScroll();
        if (_vm != null)
        {
            SyncAll();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
        DetachListScroll();
        if (_vm != null)
        {
            UnsubscribeFromVm(_vm);
        }
    }

    // ── El seguidor de fondo: de dónde sale la decisión de perseguir la cola ────────────────────

    /// <summary>
    /// Busca el <c>ScrollViewer</c> que hay dentro de la lista. Se hace al cargarse porque la plantilla del
    /// <c>ListView</c> aún no existe en el constructor: sin este visor no hay forma de SABER si la consola
    /// está al fondo, y la única alternativa —moverla siempre— es justo el defecto que se está curando.
    /// </summary>
    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scroll)
        {
            return scroll;
        }

        int count;
        try
        {
            count = VisualTreeHelper.GetChildrenCount(root);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null; // rama en plena reconstrucción: se reintenta en el próximo Loaded
        }

        for (int i = 0; i < count; i++)
        {
            if (FindDescendantScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void AttachListScroll()
    {
        if (_listScroll != null)
        {
            return;
        }

        _listScroll = FindDescendantScrollViewer(LogListView);
        if (_listScroll == null)
        {
            return;
        }

        _listScroll.ViewChanged += OnListScrollChanged;
        _lastOffset = _listScroll.VerticalOffset;
        _lastExtent = _listScroll.ExtentHeight;
    }

    private void DetachListScroll()
    {
        if (_listScroll == null)
        {
            return;
        }

        _listScroll.ViewChanged -= OnListScrollChanged;
        _listScroll = null;
    }

    /// <summary>¿Estamos al fondo (con tolerancia de medio renglón) también cuando la lista ni siquiera desborda?</summary>
    private bool IsAtBottom()
    {
        var scroll = _listScroll;
        return scroll == null || scroll.VerticalOffset >= scroll.ScrollableHeight - 8;
    }

    /// <summary>
    /// El único sitio donde el seguimiento se APAGA: el usuario subió (el extent no cambió y el offset
    /// bajó). Cualquier otra causa de desplazamiento —un filtro, la recarga del histórico, el repliegue
    /// del anillo al llenarse— va acompañada de un cambio de extent y aquí se ignora, para que la consola
    /// no deje de seguir la cola porque la lista se hubiera reconstruido.
    /// </summary>
    private void OnListScrollChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        var scroll = _listScroll;
        if (scroll == null)
        {
            return;
        }

        double offset = scroll.VerticalOffset;
        double extent = scroll.ExtentHeight;
        bool extentChanged = Math.Abs(extent - _lastExtent) > 0.5;

        if (IsAtBottom())
        {
            if (!_followTail || _unseenCount > 0)
            {
                _followTail = true;
                _unseenCount = 0;
                UpdateNewLogsPill();
            }
        }
        else if (_followTail && !extentChanged && offset < _lastOffset - 0.5)
        {
            _followTail = false;
            UpdateNewLogsPill();
        }

        _lastOffset = offset;
        _lastExtent = extent;
    }

    private void OnLanguageChanged(object? sender, System.Globalization.CultureInfo e)
    {
        DispatcherQueue.TryEnqueue(ApplyLocalizedTexts);
    }

    private void ApplyLocalizedTexts()
    {
        var loc = LocalizationManager.Instance;
        TxtTitle.Text = loc.GetString("ExecutionConsoleTitle", "💻 Consola de Ejecución");
        TxtFilterAll.Text = loc.GetString("LogFilterAll", "Todos");
        TxtFilterErrors.Text = loc.GetString("LogFilterErrors", "Errores");
        TxtFilterWarnings.Text = loc.GetString("LogFilterWarnings", "Avisos");
        TxtFilterInfo.Text = loc.GetString("LogFilterInfo", "Info");
        TxtFilterDebug.Text = loc.GetString("LogFilterDebug", "Debug");
        SearchBox.PlaceholderText = loc.GetString("LogSearchPlaceholder", "Buscar logs...");
        TxtLive.Text = loc.GetString("LogLive", "⚡ En Vivo");
        TxtExport.Text = loc.GetString("LogExport", "💾 Exportar");
        TxtClear.Text = loc.GetString("LogClear", "🗑 Limpiar");
        NewLogsPillLabel.Text = loc.GetString("LogNewRecords", "nuevos registros");
    }

    private void SubscribeToVm(LogViewModel vm)
    {
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.OnLogBatchAdded += OnLogBatchAdded;
        vm.OnFilterChanged += OnFilterChanged;
        vm.OnLogsCleared += OnLogsCleared;
    }

    private void UnsubscribeFromVm(LogViewModel vm)
    {
        vm.PropertyChanged -= OnVmPropertyChanged;
        vm.OnLogBatchAdded -= OnLogBatchAdded;
        vm.OnFilterChanged -= OnFilterChanged;
        vm.OnLogsCleared -= OnLogsCleared;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_vm == null) return;

            switch (e.PropertyName)
            {
                case nameof(LogViewModel.ErrorCount):
                case nameof(LogViewModel.WarningCount):
                case nameof(LogViewModel.InfoCount):
                case nameof(LogViewModel.DebugCount):
                case nameof(LogViewModel.TotalLogsCount):
                    UpdateCounts();
                    break;
                case nameof(LogViewModel.ActiveFilter):
                    UpdateFilterHighlights();
                    break;
                case nameof(LogViewModel.IsLiveMode):
                    BtnLiveToggle.IsChecked = _vm.IsLiveMode;
                    break;
                case nameof(LogViewModel.ProgressPercentage):
                    UpdateProgress();
                    break;
            }
        });
    }

    private void UpdateCounts()
    {
        if (_vm == null) return;

        TxtErrorCount.Text = _vm.ErrorCount.ToString();
        BadgeErrors.Visibility = _vm.ErrorCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        TxtWarningCount.Text = _vm.WarningCount.ToString();
        BadgeWarnings.Visibility = _vm.WarningCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        TxtInfoCount.Text = _vm.InfoCount.ToString();
        BadgeInfo.Visibility = _vm.InfoCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        TxtDebugCount.Text = _vm.DebugCount.ToString();
        BadgeDebug.Visibility = _vm.DebugCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        TxtTotalLogs.Text = $"{_vm.TotalLogsCount:N0} logs";
    }

    private void UpdateProgress()
    {
        if (_vm == null) return;
        double progress = _vm.ProgressPercentage;
        if (progress > 0 && progress < 100)
        {
            SlimProgressBar.Value = progress;
            SlimProgressBar.Visibility = Visibility.Visible;
        }
        else
        {
            SlimProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateFilterHighlights()
    {
        if (_vm == null || _isUpdatingFilterButtons) return;
        _isUpdatingFilterButtons = true;

        try
        {
            var active = _vm.ActiveFilter;
            var accentBrush = (Brush)Application.Current.Resources["CanvasAccentPrimaryBrush"];
            var surfaceBrush = (Brush)Application.Current.Resources["CanvasSurfaceBrush"];
            var borderBrush = (Brush)Application.Current.Resources["CanvasBorderBrush"];

            ResetButton(BtnFilterAll, active == LogFilterLevel.All, accentBrush, surfaceBrush, borderBrush);
            ResetButton(BtnFilterErrors, active == LogFilterLevel.ErrorsOnly, accentBrush, surfaceBrush, borderBrush);
            ResetButton(BtnFilterWarnings, active == LogFilterLevel.WarningsOnly, accentBrush, surfaceBrush, borderBrush);
            ResetButton(BtnFilterInfo, active == LogFilterLevel.InfoOnly, accentBrush, surfaceBrush, borderBrush);
            ResetButton(BtnFilterDebug, active == LogFilterLevel.DebugOnly, accentBrush, surfaceBrush, borderBrush);
        }
        finally
        {
            _isUpdatingFilterButtons = false;
        }
    }

    private static void ResetButton(Button btn, bool isActive, Brush accent, Brush surface, Brush border)
    {
        btn.BorderBrush = isActive ? accent : border;
        btn.BorderThickness = isActive ? new Thickness(1.5) : new Thickness(1);
    }

    private void SyncAll()
    {
        if (_vm == null) return;
        UpdateCounts();
        UpdateFilterHighlights();
        UpdateProgress();
        BtnLiveToggle.IsChecked = _vm.IsLiveMode;
        PopulateList();
    }

    /// <summary>
    /// Reconstruye la lista visible entera. Sólo para lo que exige reconstruirla de verdad —la carga
    /// inicial, un cambio de filtro, la limpieza—: reconstruir en cada lote era el tirón que se veía
    /// cuando el buffer llenaba sus 2000 registros (Clear + re-add = la vista salta al principio).
    /// </summary>
    private void PopulateList()
    {
        if (_vm == null) return;

        _displayedItems.Clear();
        foreach (var log in _vm.Logs)
        {
            _displayedItems.Add(new LogItemViewModel(log));
        }

        // Un cambio de filtro o de filtro de búsqueda entrega una lista NUEVA: lo que había «sin ver»
        // deja de existir y la píldora no puede seguir contando viejos.
        _unseenCount = 0;
        ScrollToBottomIfLive();
        UpdateNewLogsPill();
    }

    /// <summary>
    /// Sincroniza la lista visible con el buffer del VM SIN reconstruirla, devolviendo cuántos registros
    /// se han añadido (lo que la píldora cuenta). Tres casos, en orden:
    ///
    /// <para>1) <b>Cabeza común</b> —lo normal—: la lista visible es un prefijo del buffer, así que sólo
    /// se añade por la cola. Es el camino que antes no existía: cualquier lote de más de 50 registros
    /// (o el buffer lleno, que dispara el desajuste de cuentas) caía en <c>PopulateList()</c> y
    /// reconstruía los 2000 ítems.</para>
    ///
    /// <para>2) <b>El anillo giró</b>: el buffer de 2000 registros suelta por la CABEZA los que no caben,
    /// así que el registro que ahora encabeza el VM está más abajo en la lista visible. Se quitan los
    /// que ya no existen —un borrado por la cabeza, no una reconstrucción— y se añade lo nuevo.</para>
    ///
    /// <para>3) <b>Nada alineable</b> —limpieza, recarga de histórico, cambio de filtro—: ahí sí se
    /// reconstruye, que es lo que <see cref="PopulateList"/> sabe hacer.</para>
    /// </summary>
    private int SyncDisplayedItems()
    {
        if (_vm == null) return 0;

        var logs = _vm.Logs;
        if (logs.Count == 0)
        {
            if (_displayedItems.Count > 0)
            {
                _displayedItems.Clear();
            }

            return 0;
        }

        if (_displayedItems.Count > 0)
        {
            int drift = IndexOfFirstRecord(logs[0]);
            if (drift >= 0 && _displayedItems.Count - drift <= logs.Count)
            {
                for (int i = 0; i < drift; i++)
                {
                    _displayedItems.RemoveAt(0);
                }

                int appended = 0;
                for (int i = _displayedItems.Count; i < logs.Count; i++)
                {
                    _displayedItems.Add(new LogItemViewModel(logs[i]));
                    appended++;
                }

                return appended;
            }
        }

        // Reconstrucción (el caso 3): PopulateList ya dejan la lista igual que el VM y pone la píldora
        // a cero, así que aquí NO se cuentan registros como «sin ver» — serían los 2000 de la lista entera.
        PopulateList();
        return 0;
    }

    /// <summary>La posición de ese registro en la lista visible, o −1 si ya no está (el anillo lo soltó).</summary>
    private int IndexOfFirstRecord(StructuredLogRecord first)
    {
        for (int i = 0; i < _displayedItems.Count; i++)
        {
            if (ReferenceEquals(_displayedItems[i].Record, first))
            {
                return i;
            }
        }

        return -1;
    }

    private void OnLogBatchAdded()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_vm == null) return;

            // La decisión de perseguir la cola se toma ANTES de tocar la lista: al añadir, el extent
            // crece y el ViewChanged puede reordenar el seguimiento justo cuando se le pregunta.
            bool following = _followTail && _vm.IsLiveMode;

            int appended = SyncDisplayedItems();

            if (following && _displayedItems.Count > 0)
            {
                LogListView.ScrollIntoView(_displayedItems[^1]);
                _unseenCount = 0;
            }
            else
            {
                _unseenCount += appended;
            }

            UpdateNewLogsPill();
        });
    }

    private void OnFilterChanged()
    {
        DispatcherQueue.TryEnqueue(PopulateList);
    }

    private void OnLogsCleared()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _displayedItems.Clear();
            UpdateCounts();
            // Sin registros no hay cola que perseguir ni viejos que contar.
            _unseenCount = 0;
            UpdateNewLogsPill();
        });
    }

    /// <summary>
    /// Sólo lleva al fondo si ya se estaba al fondo —y en vivo—. La condición incondicional anterior
    /// arrastraba la vista con cada lote aunque el usuario estuviera leyendo algo de arriba: era el
    /// «se mueve el panel de logs» que se veía con el cursor encima del catálogo.
    /// </summary>
    private void ScrollToBottomIfLive()
    {
        if (_vm?.IsLiveMode == true && _followTail && _displayedItems.Count > 0)
        {
            LogListView.ScrollIntoView(_displayedItems[^1]);
        }
    }

    /// <summary>La píldora: cuántos registros hay por debajo de donde está el usuario, y sólo eso.</summary>
    private void UpdateNewLogsPill()
    {
        bool show = _vm?.IsLiveMode == true && !_followTail && _unseenCount > 0;
        NewLogsPill.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        NewLogsPillCount.Text = _unseenCount >= 1000 ? "999+" : _unseenCount.ToString();
    }

    /// <summary>Volver al fondo es una decisión del USUARIO: por eso tiene píldora y no es automático.</summary>
    private void OnNewLogsPillClicked(object sender, RoutedEventArgs e)
    {
        _followTail = true;
        _unseenCount = 0;
        ScrollToBottomIfLive();
        UpdateNewLogsPill();
    }

    private void OnLogItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LogItemViewModel item)
        {
            item.ToggleExpanded();
        }
    }

    private void OnFilterAllClicked(object sender, RoutedEventArgs e) => _vm?.SetFilter("All");
    private void OnFilterErrorsClicked(object sender, RoutedEventArgs e) => _vm?.SetFilter("Errors");
    private void OnFilterWarningsClicked(object sender, RoutedEventArgs e) => _vm?.SetFilter("Warnings");
    private void OnFilterInfoClicked(object sender, RoutedEventArgs e) => _vm?.SetFilter("Info");
    private void OnFilterDebugClicked(object sender, RoutedEventArgs e) => _vm?.SetFilter("Debug");

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        string text = SearchBox.Text;
        BtnClearSearch.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (_vm != null)
        {
            _ = _vm.SearchAsync(text);
        }
    }

    private void OnClearSearchClicked(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        _vm?.ClearSearchFilter();
    }

    private void OnLiveToggleClicked(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _vm.IsLiveMode = BtnLiveToggle.IsChecked == true;
            if (_vm.IsLiveMode)
            {
                // Encender «En vivo» es pedir explícitamente seguir la cola: se reanuda y se borra la píldora.
                _followTail = true;
                _unseenCount = 0;
                ScrollToBottomIfLive();
                UpdateNewLogsPill();
            }
        }
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _ = _vm.ExportLogs();
        }
    }

    private void OnClearClicked(object sender, RoutedEventArgs e)
    {
        if (_vm != null)
        {
            _ = _vm.ClearLogs();
        }
    }

    private void OnCollapseClicked(object sender, RoutedEventArgs e)
    {
        CollapseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCopyMessageClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LogItemViewModel item })
        {
            item.CopyMessage();
        }
    }

    private void OnCopyFullLineClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LogItemViewModel item })
        {
            item.CopyFullLine();
        }
    }

    private void OnCopyFilePathClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LogItemViewModel item })
        {
            item.CopyFilePath();
        }
    }

    private void OnCopyJsonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LogItemViewModel item })
        {
            item.CopyJson();
        }
    }
}
