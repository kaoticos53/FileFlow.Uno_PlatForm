using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.Plugin.FileSystem.Services;
using FileFlow.Plugin.FileSystem.UI.Services;
using FileFlow.Sdk.Localization;
using FileFlow.Sdk.Services;
using FileFlow.Sdk.SyntheticData;

namespace FileFlow.Plugin.FileSystem.UI.ViewModels;

public partial class SyntheticDataSetDesignerViewModel : ObservableObject
{
    private readonly ISyntheticDataSetStorageService _storageService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private SyntheticDataSet? _selectedDataSet;

    [ObservableProperty]
    private string _dataSetName = string.Empty;

    [ObservableProperty]
    private string _dataSetCategory = "General";

    [ObservableProperty]
    private string _dataSetDescription = string.Empty;

    [ObservableProperty]
    private bool _isBuiltInSelected;

    [ObservableProperty]
    private int _selectedTabIndex = 0; // 0 = Árbol Jerárquico / Grilla, 1 = DSL Árbol, 2 = JSON

    [ObservableProperty]
    private string _dslText = string.Empty;

    [ObservableProperty]
    private string _jsonText = string.Empty;

    [ObservableProperty]
    private SyntheticFileDefinition? _selectedItem;

    [ObservableProperty]
    private SyntheticTreeNodeItem? _selectedTreeNode;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<SyntheticDataSet> FilteredDataSets { get; } = [];
    public ObservableCollection<SyntheticFileDefinition> EditableItems { get; } = [];
    public ObservableCollection<SyntheticTreeNodeItem> RootTreeNodes { get; } = [];

    public IReadOnlyList<string> AvailableCategories =>
    [
        "General",
        "Películas",
        "Series",
        "Cómics y Manga",
        "Música",
        "Fotos",
        "Documentos",
        "Personalizada"
    ];

    /// <summary>El catálogo tiene elementos tras aplicar la búsqueda (con la lista vacía se explica por qué).</summary>
    public bool HasFilteredDataSets => FilteredDataSets.Count > 0;

    /// <summary>Hay un dataset seleccionado (el panel de propiedades sólo se pinta entonces).</summary>
    public bool HasSelectedDataSet => SelectedDataSet != null;

    /// <summary>Hay un nodo seleccionado en el árbol (el inspector sólo se pinta entonces).</summary>
    public bool HasSelectedNode => SelectedTreeNode != null;

    /// <summary>El árbol tiene elementos (con el árbol vacío se muestra la invitación a añadir).</summary>
    public bool HasTreeNodes => RootTreeNodes.Count > 0;

    /// <summary>El nodo seleccionado admite una entrada interna (es un comprimido o una de sus entradas).</summary>
    public bool CanAddArchiveEntry => SelectedTreeNode is { } node && (node.IsArchive || node.IsArchiveEntry);

    public int TotalFiles => EditableItems.Count(i => !i.IsDirectory);
    public int TotalDirectories => EditableItems.Count(i => i.IsDirectory);
    public string TotalSizeFormatted => SyntheticTreeDslParser.FormatSize(EditableItems.Where(i => !i.IsDirectory).Sum(i => i.FileSizeBytes));

    public SyntheticDataSetDesignerViewModel(ISyntheticDataSetStorageService? storageService = null, IDialogService? dialogService = null)
    {
        _storageService = storageService ?? SyntheticDataSetStorageService.Instance;
        _dialogService = dialogService ?? NullDialogService.Instance;
        _storageService.DataSetsChanged += OnDataSetsChanged;
        RefreshDataSetsList();

        if (FilteredDataSets.Count > 0)
        {
            SelectedDataSet = FilteredDataSets[0];
        }
    }

    private void OnDataSetsChanged(object? sender, EventArgs e)
    {
        RefreshDataSetsList();
    }

    partial void OnSearchTextChanged(string value)
    {
        RefreshDataSetsList();
    }

    partial void OnSelectedDataSetChanged(SyntheticDataSet? value)
    {
        OnPropertyChanged(nameof(HasSelectedDataSet));

        if (value == null)
        {
            DataSetName = string.Empty;
            DataSetCategory = "General";
            DataSetDescription = string.Empty;
            IsBuiltInSelected = false;
            EditableItems.Clear();
            RootTreeNodes.Clear();
            SelectedTreeNode = null;
            DslText = string.Empty;
            JsonText = string.Empty;
            NotifyMetrics();
            return;
        }

        DataSetName = value.Name;
        DataSetCategory = value.Category;
        DataSetDescription = value.Description;
        IsBuiltInSelected = value.IsBuiltIn;

        EditableItems.Clear();
        foreach (var item in value.Items)
        {
            EditableItems.Add(item.Clone());
        }

        BuildTreeFromItems();
        SyncViewsFromItems();
        NotifyMetrics();
        StatusMessage = LocalizationManager.Instance.GetFormattedString("Msg_DataSetLoaded", "Dataset '{0}' cargado.", value.Name);
    }

    partial void OnSelectedTreeNodeChanged(SyntheticTreeNodeItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedNode));
        OnPropertyChanged(nameof(CanAddArchiveEntry));
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 0) // Árbol / Explorador
        {
            BuildTreeFromItems();
        }
        else if (value == 1) // Modo Árbol DSL
        {
            SyncItemsFromTree();
            DslText = SyntheticTreeDslParser.Serialize(EditableItems);
        }
        else if (value == 2) // Modo JSON
        {
            SyncItemsFromTree();
            RefreshJsonText();
        }
    }

    public void RefreshDataSetsList()
    {
        string? currentSelectedId = SelectedDataSet?.Id;
        var all = _storageService.GetAllDataSets();

        FilteredDataSets.Clear();
        foreach (var ds in all)
        {
            if (string.IsNullOrWhiteSpace(SearchText) ||
                ds.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                ds.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            {
                FilteredDataSets.Add(ds);
            }
        }

        if (currentSelectedId != null)
        {
            SelectedDataSet = FilteredDataSets.FirstOrDefault(d => d.Id == currentSelectedId);
        }

        OnPropertyChanged(nameof(HasFilteredDataSets));
    }

    // Métodos de construcción, ordenación y sincronización jerárquica del árbol
    // modularizados en SyntheticDataSetDesignerViewModel.Tree.cs.


    [RelayCommand]
    private void NewDataSet()
    {
        var newDs = new SyntheticDataSet("Nuevo Conjunto de Pruebas", "Personalizada", "Dataset creado a medida por el usuario.")
        {
            Items =
            [
                new SyntheticFileDefinition("Documentos/notas.txt", 2048, false),
                new SyntheticFileDefinition("Imágenes/foto_01.jpg", 3145728, false, new Dictionary<string, object?> { ["Exif:CameraModel"] = "Sony A7IV" }),
                new SyntheticFileDefinition("Archivos/paquete.zip", 1048576, false)
                {
                    SimulatedArchiveEntries =
                    [
                        new SyntheticArchiveEntryDefinition("doc.pdf", 512000),
                        new SyntheticArchiveEntryDefinition("data.csv", 12000)
                    ]
                }
            ]
        };

        _storageService.SaveDataSet(newDs);
        RefreshDataSetsList();
        SelectedDataSet = FilteredDataSets.FirstOrDefault(d => d.Id == newDs.Id);
        StatusMessage = LocalizationManager.Instance.GetString("Msg_NewDataSetCreated", "Nuevo dataset creado con éxito.");
    }

    [RelayCommand]
    private void DuplicateDataSet()
    {
        if (SelectedDataSet == null) return;

        var clone = _storageService.CloneDataSet(SelectedDataSet.Id, $"{SelectedDataSet.Name} (Copia)");
        RefreshDataSetsList();
        SelectedDataSet = FilteredDataSets.FirstOrDefault(d => d.Id == clone.Id);
        StatusMessage = LocalizationManager.Instance.GetFormattedString("Msg_DataSetDuplicated", "Copia creada: '{0}'.", clone.Name);
    }

    /// <summary>
    /// Borra un dataset propio, previa confirmación.
    ///
    /// <para>La pregunta va por <see cref="IDialogService.ConfirmAsync"/>: el borrado es permanente y la orden
    /// depende de la respuesta REAL del usuario (la vía síncrona contesta «no» desde el hilo de UI en un host
    /// WinUI: el botón no borraría nada y tampoco avisaría).</para>
    /// </summary>
    [RelayCommand]
    private async Task DeleteDataSetAsync()
    {
        if (SelectedDataSet == null || SelectedDataSet.IsBuiltIn) return;

        bool confirm = await _dialogService.ConfirmAsync(
            LocalizationManager.Instance.GetFormattedString("Msg_ConfirmDeleteDataSet", "¿Deseas eliminar de forma permanente el dataset '{0}'?", SelectedDataSet.Name),
            LocalizationManager.Instance.GetString("Title_ConfirmDelete", "Confirmar Eliminación"));

        if (confirm)
        {
            string deletedName = SelectedDataSet.Name;
            _storageService.DeleteDataSet(SelectedDataSet.Id);
            RefreshDataSetsList();
            SelectedDataSet = FilteredDataSets.FirstOrDefault();
            StatusMessage = LocalizationManager.Instance.GetFormattedString("Msg_DataSetDeleted", "Dataset '{0}' eliminado.", deletedName);
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (SelectedDataSet == null) return;

        // Si estaba en la pestaña del árbol, sincronizar items
        if (SelectedTabIndex == 0)
        {
            SyncItemsFromTree();
        }
        else if (SelectedTabIndex == 1)
        {
            ApplyDslToItems();
        }
        else if (SelectedTabIndex == 2)
        {
            ApplyJsonToItems();
        }

        if (SelectedDataSet.IsBuiltIn)
        {
            // Si intenta guardar sobre un Built-in, crear una copia editable para no romper los oficiales
            var clone = _storageService.CloneDataSet(SelectedDataSet.Id, $"{DataSetName} (Personalizado)");
            clone.Description = DataSetDescription;
            clone.Category = DataSetCategory;
            clone.Items = EditableItems.Select(i => i.Clone()).ToList();
            _storageService.SaveDataSet(clone);

            RefreshDataSetsList();
            SelectedDataSet = FilteredDataSets.FirstOrDefault(d => d.Id == clone.Id);
            StatusMessage = LocalizationManager.Instance.GetFormattedString("Msg_BuiltInClonedOnSave", "Los datasets oficiales son de solo lectura. Se ha guardado una copia personalizada: '{0}'.", clone.Name);
            return;
        }

        SelectedDataSet.Name = DataSetName;
        SelectedDataSet.Category = DataSetCategory;
        SelectedDataSet.Description = DataSetDescription;
        SelectedDataSet.Items = EditableItems.Select(i => i.Clone()).ToList();

        _storageService.SaveDataSet(SelectedDataSet);
        RefreshDataSetsList();
        NotifyMetrics();
        StatusMessage = LocalizationManager.Instance.GetFormattedString("Msg_DataSetSavedSuccess", "Dataset '{0}' guardado correctamente.", DataSetName);
    }

    [RelayCommand]
    private void AddFile()
    {
        AddFileToTree();
    }

    [RelayCommand]
    private void AddFolder()
    {
        AddFolderToTree();
    }

    [RelayCommand]
    private void RemoveItem()
    {
        RemoveTreeNode();
    }

    // Comandos de manipulación y mutación del árbol jerárquico
    // modularizados en SyntheticDataSetDesignerViewModel.Tree.cs.


    // Comandos de aplicación de DSL textual y JSON
    // modularizados en SyntheticDataSetDesignerViewModel.Dsl.cs.


    [RelayCommand]
    private async Task ExportAsync()
    {
        if (SelectedDataSet == null) return;

        SyncItemsFromTree();

        // El selector lo pone la COSTURA del plugin (UI/Services/PortableFilePicker): a este view model lo pintan
        // los hosts y sólo uno de ellos tiene toolkit con el que abrir un diálogo de ficheros.
        PickedFile? picked = await PortableFilePicker.PickSaveAsync(
            LocalizationManager.Instance.GetString("Title_ExportDataSet", "Exportar Dataset Sintético"),
            DataSetFileFilters,
            $"{SelectedDataSet.Name.Replace(" ", "_")}.json",
            "json",
            _dialogService);

        if (picked == null) return;

        var target = SelectedDataSet.Clone(DataSetName);
        target.Category = DataSetCategory;
        target.Description = DataSetDescription;
        target.Items = EditableItems.Select(i => i.Clone()).ToList();

        string json = _storageService.ExportDataSetToJson(target);
        await using var stream = picked.Stream;
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(json);
        StatusMessage = LocalizationManager.Instance.GetFormattedString("Msg_DataSetExported", "Dataset exportado a '{0}'.", Path.GetFileName(picked.Path));
    }

    /// <summary>El desplegable de tipos del selector: los mismos rótulos que la versión anterior ofrecía.</summary>
    private static readonly FileTypeFilter[] DataSetFileFilters =
    [
        new("Archivos JSON (*.json)", ["*.json"]),
        new("Todos los archivos (*.*)", ["*.*"]),
    ];

    [RelayCommand]
    private async Task ImportAsync()
    {
        PickedFile? picked = await PortableFilePicker.PickOpenAsync(
            LocalizationManager.Instance.GetString("Title_ImportDataSet", "Importar Dataset Sintético"),
            DataSetFileFilters,
            _dialogService);

        if (picked == null) return;

        try
        {
            await using var stream = picked.Stream;
            using var reader = new StreamReader(stream);
            string json = await reader.ReadToEndAsync();
            var imported = _storageService.ImportDataSetFromJson(json, autoSave: true);
            RefreshDataSetsList();
            SelectedDataSet = FilteredDataSets.FirstOrDefault(d => d.Id == imported.Id);
            StatusMessage = LocalizationManager.Instance.GetFormattedString("Msg_DataSetImported", "Dataset '{0}' importado y guardado.", imported.Name);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al importar: {ex.Message}";
        }
    }

    // Serialización y actualización de vistas DSL y JSON
    // modularizadas en SyntheticDataSetDesignerViewModel.Dsl.cs.


    public void NotifyMetrics()
    {
        OnPropertyChanged(nameof(TotalFiles));
        OnPropertyChanged(nameof(TotalDirectories));
        OnPropertyChanged(nameof(TotalSizeFormatted));
        OnPropertyChanged(nameof(HasTreeNodes));
    }
}

