using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using FileFlow.Sdk.SyntheticData;

namespace FileFlow.Plugin.FileSystem.UI.ViewModels;

public partial class SyntheticDataSetDesignerViewModel
{
    public void BuildTreeFromItems()
    {
        RootTreeNodes.Clear();
        var folderLookup = new Dictionary<string, SyntheticTreeNodeItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in EditableItems)
        {
            string rawPath = item.RelativePath.Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(rawPath)) continue;

            var segments = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;

            SyntheticTreeNodeItem? currentParent = null;
            string currentPathAccumulator = "";

            // Create intermediate folder nodes
            for (int i = 0; i < segments.Length - 1; i++)
            {
                string segment = segments[i];
                currentPathAccumulator = string.IsNullOrEmpty(currentPathAccumulator) ? segment : $"{currentPathAccumulator}/{segment}";

                if (!folderLookup.TryGetValue(currentPathAccumulator, out var folderNode))
                {
                    folderNode = new SyntheticTreeNodeItem(segment, currentPathAccumulator, isDirectory: true, sizeBytes: 0, currentParent);
                    folderLookup[currentPathAccumulator] = folderNode;

                    if (currentParent == null)
                    {
                        RootTreeNodes.Add(folderNode);
                    }
                    else
                    {
                        currentParent.Children.Add(folderNode);
                    }
                }

                currentParent = folderNode;
            }

            // Create or register the leaf item
            string leafSegment = segments[^1];
            string leafPath = string.IsNullOrEmpty(currentPathAccumulator) ? leafSegment : $"{currentPathAccumulator}/{leafSegment}";

            if (item.IsDirectory)
            {
                if (!folderLookup.TryGetValue(leafPath, out var leafFolderNode))
                {
                    leafFolderNode = new SyntheticTreeNodeItem(item, currentParent);
                    folderLookup[leafPath] = leafFolderNode;

                    if (currentParent == null)
                    {
                        RootTreeNodes.Add(leafFolderNode);
                    }
                    else
                    {
                        currentParent.Children.Add(leafFolderNode);
                    }
                }
            }
            else
            {
                var fileNode = new SyntheticTreeNodeItem(item, currentParent);
                if (fileNode.IsArchive)
                {
                    EnsureDefaultArchiveEntries(fileNode);
                    PopulateArchiveChildren(fileNode);
                }

                if (currentParent == null)
                {
                    RootTreeNodes.Add(fileNode);
                }
                else
                {
                    currentParent.Children.Add(fileNode);
                }
            }
        }

        SortTreeRecursively(RootTreeNodes);
        SelectedTreeNode = RootTreeNodes.FirstOrDefault();
    }

    private static void EnsureDefaultArchiveEntries(SyntheticTreeNodeItem fileNode)
    {
        if (!fileNode.IsArchive || fileNode.SimulatedArchiveEntries.Count > 0) return;

        string ext = Path.GetExtension(fileNode.Name).ToLowerInvariant();
        if (ext is ".cbr" or ".cbz")
        {
            int pageCount = 24;
            if (fileNode.Metadata.TryGetValue("Doc:PageCount", out var pcObj) && pcObj != null && int.TryParse(pcObj.ToString(), out int parsedPc) && parsedPc > 0)
            {
                pageCount = Math.Min(parsedPc, 30);
            }

            long pageAvgBytes = Math.Max(50000, fileNode.FileSizeBytes / Math.Max(1, pageCount));
            for (int i = 1; i <= Math.Min(pageCount, 12); i++)
            {
                fileNode.SimulatedArchiveEntries.Add(new SyntheticArchiveEntryDefinition($"page_{i:D3}.jpg", pageAvgBytes));
            }
            fileNode.SimulatedArchiveEntries.Add(new SyntheticArchiveEntryDefinition("ComicInfo.xml", 2048));
        }
        else if (ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz")
        {
            long partSize = Math.Max(1024, fileNode.FileSizeBytes / 3);
            fileNode.SimulatedArchiveEntries.Add(new SyntheticArchiveEntryDefinition("documento_interno.pdf", partSize));
            fileNode.SimulatedArchiveEntries.Add(new SyntheticArchiveEntryDefinition("datos_extra.csv", Math.Max(512, partSize / 4)));
            fileNode.SimulatedArchiveEntries.Add(new SyntheticArchiveEntryDefinition("leeme.txt", 1024));
        }
    }

    private static void PopulateArchiveChildren(SyntheticTreeNodeItem archiveNode)
    {
        archiveNode.Children.Clear();
        foreach (var entry in archiveNode.SimulatedArchiveEntries)
        {
            var entryNode = new SyntheticTreeNodeItem(
                name: Path.GetFileName(entry.InnerPath),
                relativePath: entry.InnerPath,
                isDirectory: entry.IsDirectory,
                sizeBytes: entry.FileSizeBytes,
                parent: archiveNode)
            {
                IsArchiveEntry = true,
                Metadata = new Dictionary<string, object?>(entry.Metadata, StringComparer.OrdinalIgnoreCase)
            };
            archiveNode.Children.Add(entryNode);
        }
    }

    private static void SortTreeRecursively(ObservableCollection<SyntheticTreeNodeItem> nodes)
    {
        var sorted = nodes.OrderByDescending(n => n.IsDirectory && !n.IsArchiveEntry).ThenBy(n => n.Name).ToList();
        nodes.Clear();
        foreach (var node in sorted)
        {
            nodes.Add(node);
            SortTreeRecursively(node.Children);
        }
    }

    public void SyncItemsFromTree()
    {
        EditableItems.Clear();
        void CollectNodes(IEnumerable<SyntheticTreeNodeItem> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.IsArchiveEntry)
                {
                    // Las entradas internas están contenidas dentro del paquete padre
                    continue;
                }

                if (node.IsDirectory)
                {
                    if (node.Children.Count == 0)
                    {
                        EditableItems.Add(new SyntheticFileDefinition($"{node.RelativePath}/", 0, isDirectory: true));
                    }
                    CollectNodes(node.Children);
                }
                else
                {
                    var fileDef = new SyntheticFileDefinition(node.RelativePath, node.FileSizeBytes, isDirectory: false, node.Metadata);
                    if (node.IsArchive)
                    {
                        var internalNodes = node.Children.Where(c => c.IsArchiveEntry).ToList();
                        if (internalNodes.Count > 0)
                        {
                            node.SimulatedArchiveEntries.Clear();
                            foreach (var internalNode in internalNodes)
                            {
                                var entryDef = new SyntheticArchiveEntryDefinition
                                {
                                    InnerPath = internalNode.Name,
                                    FileSizeBytes = internalNode.FileSizeBytes,
                                    IsDirectory = internalNode.IsDirectory,
                                    Metadata = new Dictionary<string, object?>(internalNode.Metadata, StringComparer.OrdinalIgnoreCase)
                                };
                                node.SimulatedArchiveEntries.Add(entryDef);
                                fileDef.SimulatedArchiveEntries.Add(entryDef);
                            }
                        }
                        else
                        {
                            foreach (var entry in node.SimulatedArchiveEntries)
                            {
                                fileDef.SimulatedArchiveEntries.Add(new SyntheticArchiveEntryDefinition
                                {
                                    InnerPath = entry.InnerPath,
                                    FileSizeBytes = entry.FileSizeBytes,
                                    IsDirectory = entry.IsDirectory,
                                    Metadata = new Dictionary<string, object?>(entry.Metadata, StringComparer.OrdinalIgnoreCase)
                                });
                            }
                        }
                    }

                    EditableItems.Add(fileDef);
                }
            }
        }
        CollectNodes(RootTreeNodes);
        SyncViewsFromItems();
        NotifyMetrics();
    }

    [RelayCommand]
    public void AddFileToTree()
    {
        if (SelectedTreeNode != null && (SelectedTreeNode.IsArchive || SelectedTreeNode.IsArchiveEntry))
        {
            var targetArchive = SelectedTreeNode.IsArchive ? SelectedTreeNode : SelectedTreeNode.Parent;
            if (targetArchive != null)
            {
                string innerPath = $"nuevo_archivo_{targetArchive.Children.Count + 1}.dat";
                var newEntry = new SyntheticArchiveEntryDefinition(innerPath, 524288);
                targetArchive.SimulatedArchiveEntries.Add(newEntry);

                var newEntryNode = new SyntheticTreeNodeItem(
                    name: innerPath,
                    relativePath: innerPath,
                    isDirectory: false,
                    sizeBytes: 524288,
                    parent: targetArchive)
                {
                    IsArchiveEntry = true
                };
                targetArchive.Children.Add(newEntryNode);
                targetArchive.IsExpanded = true;
                targetArchive.NotifyParentMetricsChanged();
                SelectedTreeNode = newEntryNode;
                SyncItemsFromTree();
                return;
            }
        }

        SyntheticTreeNodeItem? targetParent = SelectedTreeNode?.IsDirectory == true
            ? SelectedTreeNode
            : SelectedTreeNode?.Parent;

        string fileName = "nuevo_archivo.dat";
        string relativePath = targetParent != null && !string.IsNullOrWhiteSpace(targetParent.RelativePath)
            ? $"{targetParent.RelativePath.TrimEnd('/')}/{fileName}"
            : fileName;

        var newFile = new SyntheticTreeNodeItem(fileName, relativePath, isDirectory: false, sizeBytes: 1048576, targetParent);

        if (targetParent != null)
        {
            targetParent.Children.Add(newFile);
            targetParent.IsExpanded = true;
            targetParent.NotifyParentMetricsChanged();
        }
        else
        {
            RootTreeNodes.Add(newFile);
        }

        SelectedTreeNode = newFile;
        SyncItemsFromTree();
    }

    [RelayCommand]
    public void AddFolderToTree()
    {
        SyntheticTreeNodeItem? targetParent = SelectedTreeNode?.IsDirectory == true
            ? SelectedTreeNode
            : SelectedTreeNode?.Parent;

        string folderName = "Nueva_Carpeta";
        string relativePath = targetParent != null && !string.IsNullOrWhiteSpace(targetParent.RelativePath)
            ? $"{targetParent.RelativePath.TrimEnd('/')}/{folderName}"
            : folderName;

        var newFolder = new SyntheticTreeNodeItem(folderName, relativePath, isDirectory: true, sizeBytes: 0, targetParent);

        if (targetParent != null)
        {
            targetParent.Children.Add(newFolder);
            targetParent.IsExpanded = true;
            targetParent.NotifyParentMetricsChanged();
        }
        else
        {
            RootTreeNodes.Add(newFolder);
        }

        SelectedTreeNode = newFolder;
        SyncItemsFromTree();
    }

    [RelayCommand]
    public void AddArchiveToTree()
    {
        SyntheticTreeNodeItem? targetParent = SelectedTreeNode?.IsDirectory == true
            ? SelectedTreeNode
            : SelectedTreeNode?.Parent;

        string archiveName = "paquete_simulado.zip";
        string relativePath = targetParent != null && !string.IsNullOrWhiteSpace(targetParent.RelativePath)
            ? $"{targetParent.RelativePath.TrimEnd('/')}/{archiveName}"
            : archiveName;

        var newArchive = new SyntheticTreeNodeItem(archiveName, relativePath, isDirectory: false, sizeBytes: 2097152, targetParent)
        {
            IsArchive = true
        };

        newArchive.SimulatedArchiveEntries.Add(new SyntheticArchiveEntryDefinition("documento_interno.pdf", 524288));
        newArchive.SimulatedArchiveEntries.Add(new SyntheticArchiveEntryDefinition("datos_extra.csv", 32768));
        newArchive.SimulatedArchiveEntries.Add(new SyntheticArchiveEntryDefinition("leeme.txt", 1024));

        PopulateArchiveChildren(newArchive);

        if (targetParent != null)
        {
            targetParent.Children.Add(newArchive);
            targetParent.IsExpanded = true;
            targetParent.NotifyParentMetricsChanged();
        }
        else
        {
            RootTreeNodes.Add(newArchive);
        }

        SelectedTreeNode = newArchive;
        SyncItemsFromTree();
    }

    [RelayCommand]
    public void AddArchiveEntry()
    {
        var targetArchive = SelectedTreeNode?.IsArchive == true
            ? SelectedTreeNode
            : (SelectedTreeNode?.IsArchiveEntry == true ? SelectedTreeNode.Parent : null);

        if (targetArchive == null) return;

        string innerPath = $"nuevo_archivo_{targetArchive.Children.Count + 1}.dat";
        var newEntry = new SyntheticArchiveEntryDefinition(innerPath, 524288);
        targetArchive.SimulatedArchiveEntries.Add(newEntry);

        var entryNode = new SyntheticTreeNodeItem(
            name: innerPath,
            relativePath: innerPath,
            isDirectory: false,
            sizeBytes: 524288,
            parent: targetArchive)
        {
            IsArchiveEntry = true
        };
        targetArchive.Children.Add(entryNode);
        targetArchive.IsExpanded = true;
        targetArchive.NotifyParentMetricsChanged();
        SelectedTreeNode = entryNode;
        SyncItemsFromTree();
    }

    [RelayCommand]
    public void RemoveArchiveEntry(SyntheticArchiveEntryDefinition? entry)
    {
        if (entry == null || SelectedTreeNode == null) return;
        var targetArchive = SelectedTreeNode.IsArchive ? SelectedTreeNode : SelectedTreeNode.Parent;
        if (targetArchive == null) return;

        targetArchive.SimulatedArchiveEntries.Remove(entry);
        var childNode = targetArchive.Children.FirstOrDefault(c => c.Name == entry.InnerPath);
        if (childNode != null)
        {
            targetArchive.Children.Remove(childNode);
        }
        targetArchive.NotifyParentMetricsChanged();
        SyncItemsFromTree();
    }

    [RelayCommand]
    public void RemoveTreeNode()
    {
        if (SelectedTreeNode == null) return;

        var parent = SelectedTreeNode.Parent;
        if (parent != null)
        {
            parent.Children.Remove(SelectedTreeNode);
            if (SelectedTreeNode.IsArchiveEntry)
            {
                var matchingEntry = parent.SimulatedArchiveEntries.FirstOrDefault(e => e.InnerPath == SelectedTreeNode.Name);
                if (matchingEntry != null)
                {
                    parent.SimulatedArchiveEntries.Remove(matchingEntry);
                }
            }
            parent.NotifyParentMetricsChanged();
            SelectedTreeNode = parent;
        }
        else
        {
            RootTreeNodes.Remove(SelectedTreeNode);
            SelectedTreeNode = RootTreeNodes.FirstOrDefault();
        }

        SyncItemsFromTree();
    }

    [RelayCommand]
    public void ExpandAllTree()
    {
        foreach (var node in RootTreeNodes)
        {
            node.SetExpandedRecursively(true);
        }
    }

    [RelayCommand]
    public void CollapseAllTree()
    {
        foreach (var node in RootTreeNodes)
        {
            node.SetExpandedRecursively(false);
        }
    }
}
