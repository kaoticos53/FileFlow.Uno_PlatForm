using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileFlow.App.Models;
using FileFlow.App.Services;
using FileFlow.Sdk;
using Material.Icons;

namespace FileFlow.App.ViewModels;

public partial class EditorViewModel
{
    // --- Spotlight Quick-Add Search ---
    [ObservableProperty]
    private bool _isSpotlightOpen;

    [ObservableProperty]
    private string _spotlightSearchText = string.Empty;

    [ObservableProperty]
    private Point _spotlightScreenPosition = new(200, 200);

    [ObservableProperty]
    private Point _spotlightCanvasPosition = new(200, 200);

    [ObservableProperty]
    private NodeToolboxItem? _selectedSpotlightItem;

    public ObservableCollection<NodeToolboxItem> FilteredSpotlightItems { get; } = [];
    private readonly List<NodeToolboxItem> _allSpotlightItems = [];

    public void PopulateSpotlightItems()
    {
        _allSpotlightItems.Clear();
        var types = _pluginLoader.UniqueNodeTypes.ToList();
        foreach (var type in types)
        {
            string typeName = type.FullName ?? type.Name;
            IFlowNode? sampleInstance = null;
            try
            {
                sampleInstance = _pluginLoader.CreateNodeInstance(typeName);
            }
            catch { }

            var defAttr = type.GetCustomAttribute<NodeDefinitionAttribute>();
            string name = _loc.GetString(type.Name + "_Name", sampleInstance?.Name ?? defAttr?.Name ?? type.Name);
            if (name.EndsWith("_Name", StringComparison.OrdinalIgnoreCase) && sampleInstance != null && !string.IsNullOrWhiteSpace(sampleInstance.Name))
            {
                name = sampleInstance.Name;
            }

            string category = sampleInstance?.Category ?? defAttr?.Category ?? "General";
            string locCategory = _loc.GetString($"Category_{category}", category);

            string description = _loc.GetString(type.Name + "_Desc", sampleInstance?.Description ?? defAttr?.Description ?? string.Empty);
            if (description.EndsWith("_Desc", StringComparison.OrdinalIgnoreCase) && sampleInstance != null && !string.IsNullOrWhiteSpace(sampleInstance.Description))
            {
                description = sampleInstance.Description;
            }

            MaterialIconKind icon = NodeIconResolver.GetIconForNodeType(typeName);
            var role = defAttr?.Role ?? PipelineRole.Transform;
            var tags = defAttr?.Tags ?? Array.Empty<string>();
            var subCategory = defAttr?.SubCategory ?? string.Empty;
            string localizedRole = _loc.GetString($"Role_{role}", role.ToString());

            var item = new NodeToolboxItem(
                name,
                locCategory,
                description,
                typeName,
                icon,
                false,
                0,
                role,
                tags,
                subCategory,
                localizedRole
            );
            _allSpotlightItems.Add(item);
        }
        UpdateFilteredSpotlightItems();
    }

    partial void OnSpotlightSearchTextChanged(string value)
    {
        UpdateFilteredSpotlightItems();
    }

    private void UpdateFilteredSpotlightItems()
    {
        FilteredSpotlightItems.Clear();
        var query = SpotlightSearchText?.Trim() ?? string.Empty;
        var matches = string.IsNullOrEmpty(query)
            ? _allSpotlightItems
            : _allSpotlightItems.Where(i =>
                i.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (i.Tags != null && i.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))));

        foreach (var item in matches.Take(30))
        {
            FilteredSpotlightItems.Add(item);
        }

        SelectedSpotlightItem = FilteredSpotlightItems.FirstOrDefault();
    }

    [RelayCommand]
    public void OpenSpotlight(Point? canvasPosition = null)
    {
        PopulateSpotlightItems();
        SpotlightCanvasPosition = canvasPosition ?? new Point(
            ViewportLocation.X + (ViewportSize.Width > 0 ? (ViewportSize.Width / (2 * (ViewportZoom > 0 ? ViewportZoom : 1.0))) : 200),
            ViewportLocation.Y + (ViewportSize.Height > 0 ? (ViewportSize.Height / (2 * (ViewportZoom > 0 ? ViewportZoom : 1.0))) : 200)
        );
        SpotlightSearchText = string.Empty;
        IsSpotlightOpen = true;
    }

    [RelayCommand]
    public void CloseSpotlight()
    {
        IsSpotlightOpen = false;
        SpotlightSearchText = string.Empty;
    }
}
