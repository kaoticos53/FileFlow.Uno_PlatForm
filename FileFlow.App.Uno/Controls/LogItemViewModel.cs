using System;
using System.ComponentModel;
using FileFlow.App.Core;
using FileFlow.Sdk;
using FileFlow.Sdk.Telemetry;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FileFlow.App.Uno.Controls;

/// <summary>
/// Adaptador de presentación reactivo para una entrada de log (<see cref="StructuredLogRecord"/>).
/// Controla el estado expandido/colapsado independiente y la proyección de colores y formateo.
/// </summary>
public sealed class LogItemViewModel : INotifyPropertyChanged
{
    private readonly StructuredLogRecord _record;
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public LogItemViewModel(StructuredLogRecord record)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
    }

    public StructuredLogRecord Record => _record;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged(nameof(IsExpanded));
                OnPropertyChanged(nameof(ChevronGlyph));
                OnPropertyChanged(nameof(DetailsVisibility));
            }
        }
    }

    public Visibility DetailsVisibility => _isExpanded ? Visibility.Visible : Visibility.Collapsed;

    public string ChevronGlyph => _isExpanded ? "▾" : "▸";

    public string FormattedTimestamp => _record.Timestamp.ToString("HH:mm:ss.fff");

    public LogLevel Level => _record.Level;

    public string BadgeText => _record.BadgeText;

    public string? NodeName => _record.NodeName;

    public bool HasNode => !string.IsNullOrWhiteSpace(_record.NodeName);

    public string? FileName => _record.FileName;

    public bool HasFile => !string.IsNullOrWhiteSpace(_record.FileName);

    public string? FilePath => _record.FilePath;

    public bool HasFilePath => !string.IsNullOrWhiteSpace(_record.FilePath);

    public string ShortItemId => _record.ShortItemId;

    public string FormattedShortItemId => $"#{_record.ShortItemId}";

    public bool HasItemId => !string.IsNullOrWhiteSpace(_record.ShortItemId);

    public string DurationText => _record.DurationMs > 0 ? $"{_record.DurationMs:F1}ms" : string.Empty;

    public bool HasDuration => _record.DurationMs > 0;

    public string Message => _record.Message;

    public string FormattedLine => _record.FormattedLine;

    public bool HasDetails => _record.HasDetails;

    public string DisplayDetails => _record.DisplayDetails;

    public string? ExecutionId => _record.ExecutionId;

    public bool HasExecutionId => !string.IsNullOrWhiteSpace(_record.ExecutionId);

    public Brush LevelBadgeBackground
    {
        get
        {
            if (Application.Current?.Resources is { } res)
            {
                if (_record.Level is LogLevel.Error or LogLevel.Critical && (res.TryGetValue("CanvasErrorBrush", out var err) || res.TryGetValue("CanvasAccentErrorBrush", out err)) && err is Brush eb)
                    return eb;
                if (_record.Level == LogLevel.Warning && (res.TryGetValue("CanvasWarningBrush", out var warn) || res.TryGetValue("CanvasAccentWarningBrush", out warn)) && warn is Brush wb)
                    return wb;
                if (_record.Level == LogLevel.Information && res.TryGetValue("CanvasAccentPrimaryBrush", out var inf) && inf is Brush ib)
                    return ib;
                if (_record.Level == LogLevel.Debug && (res.TryGetValue("CanvasPurpleBrush", out var dbg) || res.TryGetValue("CanvasAccentPurpleBrush", out dbg)) && dbg is Brush dgb)
                    return dgb;
            }

            return _record.Level switch
            {
                LogLevel.Critical or LogLevel.Error => new SolidColorBrush(ColorHelper.FromArgb(255, 239, 83, 80)),
                LogLevel.Warning => new SolidColorBrush(ColorHelper.FromArgb(255, 255, 179, 0)),
                LogLevel.Information => new SolidColorBrush(ColorHelper.FromArgb(255, 38, 198, 218)),
                LogLevel.Debug => new SolidColorBrush(ColorHelper.FromArgb(255, 171, 71, 188)),
                _ => new SolidColorBrush(ColorHelper.FromArgb(255, 158, 158, 158))
            };
        }
    }

    public Brush LevelBadgeForeground => new SolidColorBrush(Colors.White);

    public void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    public void CopyJson()
    {
        if (HasDetails)
        {
            HostUi.SetClipboardText(DisplayDetails);
        }
    }

    public void CopyFullLine()
    {
        HostUi.SetClipboardText(FormattedLine);
    }

    public void CopyMessage()
    {
        HostUi.SetClipboardText(Message);
    }

    public void CopyFilePath()
    {
        if (HasFilePath)
        {
            HostUi.SetClipboardText(FilePath);
        }
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
