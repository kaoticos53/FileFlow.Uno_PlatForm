using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FileFlow.App.Core;
using FileFlow.App.Preview.Core;
using FileFlow.App.Uno.Controls;
using FileFlow.Sdk.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FileFlow.App.Uno.Platform;

/// <summary>
/// El anfitrión de vista previa de archivos del host Uno: conecta el canal portable
/// <see cref="HostUi.FilePreviewRequested"/> con la superficie modal nativa
/// (<see cref="ContentDialog"/> y <see cref="FilePreviewDialogBody"/>).
/// </summary>
public static class UnoFilePreviewHost
{
    /// <summary>El diálogo de previsualización activo actualmente (null si ninguno).</summary>
    public static ContentDialog? ActivePreviewDialog { get; private set; }

    /// <summary>El cuerpo de la previsualización activa actualmente (null si ninguna).</summary>
    public static FilePreviewDialogBody? ActivePreviewBody => ActivePreviewDialog?.Content as FilePreviewDialogBody;

    /// <summary>
    /// Muestra la vista previa del archivo solicitado dentro de la ventana principal del host.
    /// </summary>
    public static async Task ShowPreviewAsync(FilePreviewRequest request)
    {
        if (request?.Context is not FilePreviewContext previewCtx)
        {
            return;
        }

        if (App.MainWindow?.Content is not FrameworkElement rootElement)
        {
            return;
        }

        XamlRoot? root = rootElement.XamlRoot;
        if (root is null)
        {
            return;
        }

        // WinUI 3 sólo admite un ContentDialog abierto a la vez sobre una misma raíz XAML
        if (UnoWindowService.ActiveDialog is not null)
        {
            return;
        }

        List<FilePreviewContext>? siblings = request.Siblings?
            .OfType<FilePreviewContext>()
            .ToList();

        var body = new FilePreviewDialogBody(previewCtx, siblings);
        var loc = LocalizationManager.Instance;

        var dialog = new ContentDialog
        {
            Title = loc.GetString("Preview_WindowTitle", "Vista Previa de Archivo — FileFlow Studio"),
            Content = body,
            CloseButtonText = loc.GetString("Preview_Close", "Cerrar (Esc)"),
            XamlRoot = root,
            MaxWidth = 1100,
            MaxHeight = 850,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 1100.0;
        dialog.Resources["ContentDialogMaxHeight"] = 850.0;

        AutomationProperties.SetAutomationId(dialog, "FilePreviewDialog");

        body.RequestClose += () => dialog.Hide();

        ActivePreviewDialog = dialog;
        try
        {
            await UnoWindowService.RunOwnedAsync(dialog);
        }
        finally
        {
            ActivePreviewDialog = null;
        }
    }
}
