using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileFlow.App.Core;
using FileFlow.App.Preview.Core;
using FileFlow.Sdk.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FileFlow.App.Uno.Controls;

/// <summary>
/// El cuerpo del previsualizador de archivos del host Uno.
/// Presenta vistas especializadas para imágenes, código/texto en monoespaciado, y archivos
/// genéricos o binarios, junto a la ficha de metadatos, navegación entre hermanos y
/// comparación visual entre original y procesado.
/// </summary>
public sealed partial class FilePreviewDialogBody : UserControl
{
    private static readonly HashSet<string> s_imageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp", ".tif", ".tiff", ".svg"
    };

    private static readonly HashSet<string> s_textExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".json", ".xml", ".csv", ".tsv", ".log", ".md", ".cs", ".py", ".js", ".ts",
        ".html", ".htm", ".css", ".yaml", ".yml", ".ini", ".cfg", ".conf", ".sql", ".sh",
        ".bat", ".ps1", ".env", ".cmd"
    };

    public partial record MetadataEntry(string Key, string Value);

    private FilePreviewContext _context;
    private readonly List<FilePreviewContext> _siblings;
    private int _currentIndex;
    private string _displayedPath = string.Empty;
    private bool _isViewingOriginal;

    /// <summary>Evento emitido para solicitar el cierre del diálogo modal contenedor.</summary>
    public event Action? RequestClose;

    public FilePreviewDialogBody(FilePreviewContext context, IReadOnlyList<FilePreviewContext>? siblings = null)
    {
        InitializeComponent();

        _context = context ?? throw new ArgumentNullException(nameof(context));
        _siblings = siblings?.ToList() ?? [_context];
        _currentIndex = _siblings.FindIndex(s =>
            string.Equals(s.CurrentPath, _context.CurrentPath, StringComparison.OrdinalIgnoreCase));
        if (_currentIndex < 0)
        {
            _currentIndex = 0;
            if (!_siblings.Contains(_context))
            {
                _siblings.Insert(0, _context);
            }
        }

        RefreshLocalization();
        LoadCurrentContext();
    }

    /// <summary>El contexto de previsualización activo actualmente.</summary>
    public FilePreviewContext CurrentContext => _context;

    /// <summary>La ruta del archivo mostrado en pantalla (puede ser el procesado o el original).</summary>
    public string DisplayedPath => _displayedPath;

    /// <summary>Actualiza las etiquetas y textos según el idioma vigente.</summary>
    public void RefreshLocalization()
    {
        var loc = LocalizationManager.Instance;

        LabelProcessed.Text = loc.GetString("Preview_BadgeProcessed", "Procesado");
        LabelOriginal.Text = loc.GetString("Preview_BadgeOriginal", "Original");
        MetadataTitleText.Text = loc.GetString("Preview_MetadataTitle", "Metadatos e Información");
        CopyPathLabel.Text = loc.GetString("Preview_CopyPath", "📋 Copiar Ruta");
        OpenExplorerLabel.Text = loc.GetString("Preview_OpenExplorer", "📂 Explorador");
        OpenFileLabel.Text = loc.GetString("Preview_OpenFile", "🚀 Abrir Archivo");
        CloseLabel.Text = loc.GetString("Preview_Close", "Cerrar");
        GenericOpenButtonText.Text = loc.GetString("Preview_OpenFile", "🚀 Abrir con aplicación del sistema");

        ToolTipService.SetToolTip(PrevSiblingButton, loc.GetString("Preview_PreviousFileToolTip", "Archivo Anterior (Flecha Izquierda)"));
        ToolTipService.SetToolTip(NextSiblingButton, loc.GetString("Preview_NextFileToolTip", "Archivo Siguiente (Flecha Derecha)"));

        UpdateSiblingCounter();
    }

    private void LoadCurrentContext()
    {
        _isViewingOriginal = false;
        _displayedPath = _context.CurrentPath;

        UpdateHeaderInfo();
        UpdateComparisonControls();
        UpdateSiblingNavigation();
        UpdateMetadataSheet();

        _ = RenderPreviewContentAsync(_displayedPath);
    }

    private void UpdateHeaderInfo()
    {
        string ext = Path.GetExtension(_displayedPath).ToLowerInvariant();
        FileNameText.Text = Path.GetFileName(_displayedPath);
        ToolTipService.SetToolTip(FileNameText, _displayedPath);
        DisplayedPathText.Text = _displayedPath;
        ToolTipService.SetToolTip(DisplayedPathText, _displayedPath);

        ExtensionBadgeText.Text = string.IsNullOrEmpty(ext) ? ".FILE" : ext.ToUpperInvariant();
        FileIconText.Text = GetFileIcon(ext);

        long sizeBytes = _context.FileSizeBytes;
        if (File.Exists(_displayedPath))
        {
            try
            {
                sizeBytes = new FileInfo(_displayedPath).Length;
            }
            catch { }
        }

        FileSizeBadgeText.Text = FormatBytes(sizeBytes);
        DimensionsBadgeBorder.Visibility = Visibility.Collapsed;

        // Si los metadatos traen dimensiones registradas, las mostramos en la insignia de inmediato
        if (_context.Metadata.TryGetValue("Dimensions", out var dims) && dims != null)
        {
            DimensionsBadgeText.Text = dims.ToString();
            DimensionsBadgeBorder.Visibility = Visibility.Visible;
        }
        else if (_context.Metadata.TryGetValue("Width", out var w) && _context.Metadata.TryGetValue("Height", out var h))
        {
            DimensionsBadgeText.Text = $"{w} × {h} px";
            DimensionsBadgeBorder.Visibility = Visibility.Visible;
        }
    }

    private void UpdateComparisonControls()
    {
        if (_context.HasOriginalComparison)
        {
            ComparisonPanel.Visibility = Visibility.Visible;
            UpdateComparisonButtonsStyle();
        }
        else
        {
            ComparisonPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateComparisonButtonsStyle()
    {
        var accentBrush = (Brush)Application.Current.Resources["CanvasAccentPrimaryBrush"];
        var cardBrush = (Brush)Application.Current.Resources["CanvasCardBrush"];
        var onAccentBrush = (Brush)Application.Current.Resources["CanvasOnAccentBrush"];
        var textBrush = (Brush)Application.Current.Resources["CanvasTextBrush"];

        if (!_isViewingOriginal)
        {
            ToggleProcessedButton.Background = accentBrush;
            LabelProcessed.Foreground = onAccentBrush;
            ToggleOriginalButton.Background = cardBrush;
            LabelOriginal.Foreground = textBrush;
        }
        else
        {
            ToggleOriginalButton.Background = accentBrush;
            LabelOriginal.Foreground = onAccentBrush;
            ToggleProcessedButton.Background = cardBrush;
            LabelProcessed.Foreground = textBrush;
        }
    }

    private void UpdateSiblingNavigation()
    {
        if (_siblings.Count > 1)
        {
            NavigationPanel.Visibility = Visibility.Visible;
            PrevSiblingButton.IsEnabled = _currentIndex > 0;
            NextSiblingButton.IsEnabled = _currentIndex < _siblings.Count - 1;
            UpdateSiblingCounter();
        }
        else
        {
            NavigationPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSiblingCounter()
    {
        string ofWord = LocalizationManager.Instance.GetString("Preview_Of", "de");
        SiblingCounterText.Text = $"{_currentIndex + 1} {ofWord} {_siblings.Count}";
    }

    private void UpdateMetadataSheet()
    {
        if (_context.Metadata.Count > 0)
        {
            MetadataColumn.Width = new GridLength(280);
            MetadataPanel.Visibility = Visibility.Visible;

            var entries = _context.Metadata
                .OrderBy(kv => kv.Key)
                .Select(kv => new MetadataEntry(kv.Key, kv.Value?.ToString() ?? string.Empty))
                .ToList();

            MetadataListView.ItemsSource = entries;
        }
        else
        {
            MetadataColumn.Width = new GridLength(0);
            MetadataPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async Task RenderPreviewContentAsync(string filePath)
    {
        ImageViewerPanel.Visibility = Visibility.Collapsed;
        TextViewerPanel.Visibility = Visibility.Collapsed;
        GenericViewerPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            ShowGenericPreview(filePath, isMissing: true);
            return;
        }

        string ext = Path.GetExtension(filePath).ToLowerInvariant();

        if (s_imageExtensions.Contains(ext))
        {
            await RenderImagePreviewAsync(filePath);
        }
        else if (s_textExtensions.Contains(ext))
        {
            await RenderTextPreviewAsync(filePath);
        }
        else
        {
            ShowGenericPreview(filePath, isMissing: false);
        }
    }

    private async Task RenderImagePreviewAsync(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.ImageOpened += (s, e) =>
            {
                if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
                {
                    DimensionsBadgeText.Text = $"{bitmap.PixelWidth} × {bitmap.PixelHeight} px";
                    DimensionsBadgeBorder.Visibility = Visibility.Visible;
                }
            };

            bitmap.UriSource = new Uri(Path.GetFullPath(filePath));
            PreviewImage.Source = bitmap;

            ImageViewerPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            ShowGenericPreview(filePath, isMissing: false);
        }

        await Task.CompletedTask;
    }

    private async Task RenderTextPreviewAsync(string filePath)
    {
        const int maxBytesToRead = 200 * 1024; // 200 KB máximo de seguridad para la UI
        string content;
        bool isTruncated = false;

        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long fileLength = stream.Length;
            int bytesToRead = (int)Math.Min(fileLength, maxBytesToRead);

            byte[] buffer = new byte[bytesToRead];
            int read = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead));

            using var reader = new StreamReader(new MemoryStream(buffer, 0, read));
            content = await reader.ReadToEndAsync();
            isTruncated = fileLength > maxBytesToRead;
        }
        catch (Exception ex)
        {
            content = $"[Error al leer el archivo de texto: {ex.Message}]";
        }

        PreviewTextBox.Text = content;

        int lines = content.Count(c => c == '\n') + 1;
        var loc = LocalizationManager.Instance;
        string linesFmt = loc.GetString("Preview_Lines", "Líneas: {0} · Caracteres: {1}");
        TextStatsLabel.Text = string.Format(linesFmt, lines, content.Length);

        if (isTruncated)
        {
            string truncFmt = loc.GetString("Preview_Truncated", "(Mostrando los primeros {0} KB)");
            TextTruncatedWarning.Text = string.Format(truncFmt, 200);
            TextTruncatedWarning.Visibility = Visibility.Visible;
        }
        else
        {
            TextTruncatedWarning.Visibility = Visibility.Collapsed;
        }

        TextViewerPanel.Visibility = Visibility.Visible;
    }

    private void ShowGenericPreview(string filePath, bool isMissing)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        GenericFileIcon.Text = GetFileIcon(ext);
        GenericFileName.Text = Path.GetFileName(filePath);

        var loc = LocalizationManager.Instance;
        if (isMissing)
        {
            GenericFileDescription.Text = loc.GetString("Preview_NoValidFile", "No hay un archivo válido generado o seleccionado para previsualizar.");
            GenericFileInfo.Text = string.Empty;
            GenericOpenButton.IsEnabled = false;
        }
        else
        {
            GenericFileDescription.Text = loc.GetString("Preview_GenericBinaryDesc", "La previsualización directa no está disponible para este formato de archivo binario.");
            try
            {
                var fi = new FileInfo(filePath);
                GenericFileInfo.Text = $"{FormatBytes(fi.Length)} · Creado: {fi.CreationTime:g} · Modificado: {fi.LastWriteTime:g}";
                GenericOpenButton.IsEnabled = true;
            }
            catch
            {
                GenericFileInfo.Text = string.Empty;
            }
        }

        GenericViewerPanel.Visibility = Visibility.Visible;
    }

    private void OnToggleProcessedClicked(object sender, RoutedEventArgs e)
    {
        if (!_isViewingOriginal) return;

        _isViewingOriginal = false;
        _displayedPath = _context.CurrentPath;
        UpdateHeaderInfo();
        UpdateComparisonButtonsStyle();
        _ = RenderPreviewContentAsync(_displayedPath);
    }

    private void OnToggleOriginalClicked(object sender, RoutedEventArgs e)
    {
        if (_isViewingOriginal || string.IsNullOrWhiteSpace(_context.OriginalPath)) return;

        _isViewingOriginal = true;
        _displayedPath = _context.OriginalPath;
        UpdateHeaderInfo();
        UpdateComparisonButtonsStyle();
        _ = RenderPreviewContentAsync(_displayedPath);
    }

    private void OnPrevSiblingClicked(object sender, RoutedEventArgs e)
    {
        if (_currentIndex > 0)
        {
            _currentIndex--;
            _context = _siblings[_currentIndex];
            LoadCurrentContext();
        }
    }

    private void OnNextSiblingClicked(object sender, RoutedEventArgs e)
    {
        if (_currentIndex < _siblings.Count - 1)
        {
            _currentIndex++;
            _context = _siblings[_currentIndex];
            LoadCurrentContext();
        }
    }

    private void OnCopyPathClicked(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_displayedPath))
        {
            HostUi.SetClipboardText(_displayedPath);
        }
    }

    private void OnOpenExplorerClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_displayedPath) || !File.Exists(_displayedPath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_displayedPath}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnOpenFileClicked(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_displayedPath) || !File.Exists(_displayedPath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _displayedPath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        RequestClose?.Invoke();
    }

    private static string GetFileIcon(string ext) => ext switch
    {
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".ico" or ".svg" => "🖼️",
        ".txt" or ".log" or ".md" or ".rtf" => "📝",
        ".json" or ".xml" or ".yaml" or ".yml" or ".csv" or ".tsv" => "📊",
        ".cs" or ".py" or ".js" or ".ts" or ".html" or ".css" or ".sh" or ".bat" or ".ps1" => "💻",
        ".mp3" or ".wav" or ".ogg" or ".flac" or ".aac" or ".m4a" => "🎵",
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".webm" => "🎬",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" => "📦",
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" => "📑",
        _ => "📄"
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024m) >= 1 && counter < suffixes.Length - 1)
        {
            number /= 1024m;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }
}
