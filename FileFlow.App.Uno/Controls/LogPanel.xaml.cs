using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using FileFlow.App.ViewModels;
using FileFlow.Sdk.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FileFlow.App.Uno.Controls;

public sealed partial class LogPanel : UserControl
{
    private LogViewModel? _vm;
    private readonly ObservableCollection<LogItemViewModel> _displayedItems = new();
    private bool _isUpdatingFilterButtons;

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
        LogListView.ItemsSource = _displayedItems;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalizedTexts();
        LocalizationManager.Instance.LanguageChanged += OnLanguageChanged;
        if (_vm != null)
        {
            SyncAll();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LocalizationManager.Instance.LanguageChanged -= OnLanguageChanged;
        if (_vm != null)
        {
            UnsubscribeFromVm(_vm);
        }
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

    private void PopulateList()
    {
        if (_vm == null) return;

        _displayedItems.Clear();
        foreach (var log in _vm.Logs)
        {
            _displayedItems.Add(new LogItemViewModel(log));
        }

        ScrollToBottomIfLive();
    }

    private void OnLogBatchAdded()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_vm == null) return;

            // En modo en vivo, añadimos los nuevos elementos al final de la lista
            int countDiff = _vm.Logs.Count - _displayedItems.Count;
            if (countDiff > 0 && countDiff <= 50)
            {
                for (int i = _displayedItems.Count; i < _vm.Logs.Count; i++)
                {
                    _displayedItems.Add(new LogItemViewModel(_vm.Logs[i]));
                }
            }
            else
            {
                PopulateList();
            }

            ScrollToBottomIfLive();
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
        });
    }

    private void ScrollToBottomIfLive()
    {
        if (_vm?.IsLiveMode == true && _displayedItems.Count > 0)
        {
            LogListView.ScrollIntoView(_displayedItems[^1]);
        }
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
                ScrollToBottomIfLive();
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
