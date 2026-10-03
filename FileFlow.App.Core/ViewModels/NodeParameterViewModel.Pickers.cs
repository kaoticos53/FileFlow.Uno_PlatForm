using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Models;
using FileFlow.App.Services;
using FileFlow.Sdk;
using FileFlow.Sdk.Services;

namespace FileFlow.App.ViewModels;

public partial class NodeParameterViewModel
{
    private EditorViewModel? ResolveEditor()
    {
        if (NodeOwner?.ParentEditor != null)
        {
            return NodeOwner.ParentEditor;
        }
        return HostEditor;
    }

    [RelayCommand]
    public void OpenVariablePicker(object? targetObject)
    {
        var editor = ResolveEditor();

        if (editor != null)
        {
            RefreshAvailableVariables(editor);
        }
        else if (AvailableVariables.Count == 0)
        {
            AvailableVariables = Services.VariableDiscoveryService.Instance.GetAvailableVariables(NodeOwner, []);
        }

        // El menú es puro dato: el host lo pinta con sus controles nativos (PopupMenuService del host).
        var items = new List<PopupMenuItem>
        {
            new()
            {
                Header = _loc.GetString("VarPicker_OpenFullCatalog", "🔍 Abrir Catálogo Completo de Variables..."),
                IsBold = true,
                Command = OpenVariableCatalogCommand,
                CommandParameter = null,
            },
        };

        foreach (var group in AvailableVariables)
        {
            if (group.Variables.Count == 0) continue;

            items.Add(new PopupMenuItem
            {
                Header = $"{group.GroupName} ({group.Variables.Count})",
                IsBold = group.IsUpstream,
                Items = group.Variables
                    .Select(v => new PopupMenuItem
                    {
                        Header = $"{v.Token}  —  {v.Description}",
                        Command = InsertVariableTokenCommand,
                        CommandParameter = v.Token,
                        ToolTip = string.IsNullOrEmpty(v.SampleValue) ? null : $"Ejemplo: {v.SampleValue}",
                    })
                    .ToList(),
            });
        }

        _menus.ShowMenu(new PopupMenuDescriptor { Items = items }, targetObject);
    }

    [RelayCommand]
    public void OpenVariableCatalog(object? targetObject)
    {
        var editor = ResolveEditor();

        if (editor != null)
        {
            RefreshAvailableVariables(editor);
        }
        else if (AvailableVariables.Count == 0)
        {
            AvailableVariables = Services.VariableDiscoveryService.Instance.GetAvailableVariables(NodeOwner, []);
        }

        var previewContext = (editor?.VariableDiscoveryService ?? Services.VariableDiscoveryService.Instance).CreatePreviewItem(NodeOwner);
        _ = _windows.ShowDialogAsync(DialogKeys.VariablePicker, new VariablePickerRequest(AvailableVariables, NodeOwner, previewContext, _loc))
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result is { Confirmed: true, Value: { } token })
                {
                    _uiDispatcher.Post(() => InsertVariableToken(token));
                }
            }, TaskScheduler.Default);
    }

    [RelayCommand]
    public void RefreshAvailableVariables(EditorViewModel editor)
    {
        if (editor != null)
        {
            if (NodeOwner != null)
            {
                AvailableVariables = editor.GetUpstreamAvailableVariables(NodeOwner);
            }
            else
            {
                AvailableVariables = editor.VariableDiscoveryService.GetAvailableVariables(null, editor.Connections);
            }
        }
    }

    [RelayCommand]
    public void InsertVariableToken(string token)
    {
        string currentVal = Value?.ToString() ?? string.Empty;
        Value = currentVal + token;
    }

    [RelayCommand]
    public void BrowsePath()
    {
        var (title, filter) = BrowsePathRequest();
        var picked = IsFolderPath
            ? _files.ShowFolderBrowserDialog(title)
            : _files.ShowOpenFileDialog(title, filter);
        if (!string.IsNullOrEmpty(picked))
        {
            Value = picked;
        }
    }

    /// <summary>
    /// La variante ASÍNCRONA del explorador de rutas: el mismo título, el mismo filtro y la misma decisión
    /// carpeta/fichero que <see cref="BrowsePath"/>, sin bloquear el hilo llamador.
    ///
    /// <para><b>Por qué existe</b>: el host Uno abre sus pickers DESDE el clic de UI, y allí la variante
    /// síncrona no puede funcionar — los pickers de WinRT exigen el hilo de UI y bloquearlo interbloquearía, así
    /// que el servicio de ese host devuelve null (declarado)—: el botón «…» de una fila de ruta quedaba
    /// dibujado, cableado y <b>sin efecto</b> (medido con el ratón). La variante asíncrona es la que ese host ya
    /// usa para el «Probar» del inspector, y la puerta la abre aquí el núcleo, no una copia de la lógica en el
    /// host. La versión anterior sigue usando la síncrona.</para>
    /// </summary>
    public async Task BrowsePathAsync()
    {
        var (title, filter) = BrowsePathRequest();
        var picked = IsFolderPath
            ? await _files.ShowFolderBrowserDialogAsync(title)
            : await _files.ShowOpenFileDialogAsync(title, filter);
        if (!string.IsNullOrEmpty(picked))
        {
            _uiDispatcher.Post(() => Value = picked);
        }
    }

    /// <summary>
    /// El comando de la variante asíncrona. Se publica a mano porque el generador de <c>[RelayCommand]</c>
    /// recorta el sufijo «Async» del nombre del método y generaría <c>BrowsePathCommand</c> otra vez —el
    /// mismo nombre que el síncrono—: aquí la variante asíncrona tiene que ser distinguible por quien la ata.
    /// </summary>
    private AsyncRelayCommand? _browsePathAsync;

    public IAsyncRelayCommand BrowsePathAsyncCommand => _browsePathAsync ??= new AsyncRelayCommand(BrowsePathAsync);

    /// <summary>El título y el filtro del explorador de rutas: una sola decisión para las dos variantes.</summary>
    private (string Title, string Filter) BrowsePathRequest() =>
        ($"Seleccionar {(IsFolderPath ? "directorio" : "archivo")} para '{Key}'", "Todos los archivos|*.*");

    [RelayCommand]
    public void OpenTextEditor(object? targetObject)
    {
        _ = _windows.ShowDialogAsync(DialogKeys.TextEditor, this)
            .ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result is { Confirmed: true, Value: { } text })
                {
                    _uiDispatcher.Post(() => Value = text);
                }
            }, TaskScheduler.Default);
    }
}
