using System;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using FileFlow.Plugin.FileSystem.Services;
using FileFlow.Sdk.Localization;
using FileFlow.Sdk.SyntheticData;

namespace FileFlow.Plugin.FileSystem.UI.ViewModels;

public partial class SyntheticDataSetDesignerViewModel
{
    [RelayCommand]
    private void ApplyDslToItems()
    {
        try
        {
            var parsed = SyntheticTreeDslParser.Parse(DslText);
            EditableItems.Clear();
            foreach (var item in parsed)
            {
                EditableItems.Add(item);
            }
            BuildTreeFromItems();
            NotifyMetrics();
            RefreshJsonText();
            StatusMessage = LocalizationManager.Instance.GetFormattedString("Msg_DslApplied", "Estructura DSL aplicada: {0} elementos creados.", parsed.Count);
        }
        catch (Exception ex)
        {
            _dialogService.ShowWarning(ex.Message, "Error en DSL");
        }
    }

    [RelayCommand]
    private void ApplyJsonToItems()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(JsonText)) return;
            var ds = JsonSerializer.Deserialize<SyntheticDataSet>(JsonText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (ds != null)
            {
                DataSetName = ds.Name;
                DataSetCategory = ds.Category;
                DataSetDescription = ds.Description;
                EditableItems.Clear();
                foreach (var item in ds.Items)
                {
                    EditableItems.Add(item);
                }
                BuildTreeFromItems();
                NotifyMetrics();
                DslText = SyntheticTreeDslParser.Serialize(EditableItems);
                StatusMessage = LocalizationManager.Instance.GetString("Msg_JsonApplied", "JSON aplicado con éxito al dataset.");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowWarning(ex.Message, "Error en JSON");
        }
    }

    private void SyncViewsFromItems()
    {
        DslText = SyntheticTreeDslParser.Serialize(EditableItems);
        RefreshJsonText();
    }

    private void RefreshJsonText()
    {
        var tempDs = new SyntheticDataSet(DataSetName, DataSetCategory, DataSetDescription)
        {
            Id = SelectedDataSet?.Id ?? Guid.NewGuid().ToString("N"),
            Items = EditableItems.ToList()
        };
        JsonText = JsonSerializer.Serialize(tempDs, new JsonSerializerOptions { WriteIndented = true });
    }
}
