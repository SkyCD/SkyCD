using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SkyCD.Plugin.Cathy;

/// <summary>
/// Helper to construct full hierarchical paths and folder trees from Cathy data.
/// </summary>
public static class CathyHierarchyBuilder
{
    private sealed class MutableFolder
    {
        public int FolderId { get; }
        public string Name { get; set; }
        public string FullPath { get; set; } = string.Empty;
        public int ParentFolderId { get; set; } = -1;
        public List<MutableFolder> Subfolders { get; } = [];
        public List<CathyFileEntry> Files { get; } = [];

        public MutableFolder(int folderId, string name)
        {
            FolderId = folderId;
            Name = name;
        }
    }

    /// <summary>
    /// Reconstructs the hierarchy tree and flat node list with normalized paths.
    /// </summary>
    public static (IReadOnlyList<CathyNode> FlatEntries, CathyImportSummary Summary) BuildHierarchy(CathyCatalog catalog)
    {
        var foldersById = new Dictionary<int, MutableFolder>();
        for (var i = 0; i < catalog.Folders.Count; i++)
        {
            var f = catalog.Folders[i];
            foldersById[f.FolderId] = new MutableFolder(f.FolderId, f.Name);
        }

        if (!foldersById.ContainsKey(0))
        {
            var rootName = catalog.Header.VolumeName ?? catalog.Header.Volume ?? catalog.Header.Device ?? "Root";
            foldersById[0] = new MutableFolder(0, rootName);
        }
        else if (string.IsNullOrWhiteSpace(foldersById[0].Name))
        {
            var rootName = catalog.Header.VolumeName ?? catalog.Header.Volume ?? catalog.Header.Device ?? "Root";
            foldersById[0].Name = rootName;
        }

        // Connect subdirectories and assign names/parents
        foreach (var entry in catalog.Entries)
        {
            if (entry.IsDirectory)
            {
                if (!foldersById.TryGetValue(entry.DirectoryId, out var subfolder))
                {
                    subfolder = new MutableFolder(entry.DirectoryId, entry.Name);
                    foldersById[entry.DirectoryId] = subfolder;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(subfolder.Name))
                    {
                        subfolder.Name = entry.Name;
                    }
                }

                subfolder.ParentFolderId = entry.ParentFolderId;
                if (foldersById.TryGetValue(entry.ParentFolderId, out var parentFolder))
                {
                    if (!parentFolder.Subfolders.Contains(subfolder))
                    {
                        parentFolder.Subfolders.Add(subfolder);
                    }
                }
            }
            else
            {
                if (foldersById.TryGetValue(entry.ParentFolderId, out var parentFolder))
                {
                    parentFolder.Files.Add(entry);
                }
                else
                {
                    foldersById[0].Files.Add(entry);
                }
            }
        }

        // Build full paths
        var root = foldersById[0];
        root.FullPath = root.Name;

        void AssignPaths(MutableFolder folder, string parentPath)
        {
            folder.FullPath = string.IsNullOrEmpty(parentPath) ? folder.Name : $"{parentPath}/{folder.Name}";
            foreach (var sub in folder.Subfolders)
            {
                AssignPaths(sub, folder.FullPath);
            }
        }

        foreach (var sub in root.Subfolders)
        {
            AssignPaths(sub, root.FullPath);
        }

        // Flatten entries
        var flatList = new List<CathyNode>();
        long totalSize = 0;
        int totalFiles = 0;

        // Add root node
        flatList.Add(new CathyNode(
            Name: root.Name,
            FullPath: root.FullPath,
            Size: 0,
            IsDirectory: true,
            ModifiedDate: catalog.Header.CatalogDate?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            Children: []));

        void CollectNodes(MutableFolder folder)
        {
            foreach (var sub in folder.Subfolders)
            {
                flatList.Add(new CathyNode(
                    Name: sub.Name,
                    FullPath: sub.FullPath,
                    Size: 0,
                    IsDirectory: true,
                    ModifiedDate: null,
                    Children: []));

                CollectNodes(sub);
            }

            foreach (var file in folder.Files)
            {
                totalFiles++;
                totalSize += file.Size;
                var filePath = $"{folder.FullPath}/{file.Name}";
                flatList.Add(new CathyNode(
                    Name: file.Name,
                    FullPath: filePath,
                    Size: file.Size,
                    IsDirectory: false,
                    ModifiedDate: file.ModifiedDate?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    Children: []));
            }
        }

        CollectNodes(root);

        var diagnostics = new List<string>();
        if (catalog.Header.CatalogDate.HasValue)
        {
            diagnostics.Add($"Catalog Date: {catalog.Header.CatalogDate.Value:yyyy-MM-dd HH:mm:ss}");
        }
        if (!string.IsNullOrWhiteSpace(catalog.Header.SerialNumber))
        {
            diagnostics.Add($"Serial Number: {catalog.Header.SerialNumber}");
        }
        if (!string.IsNullOrWhiteSpace(catalog.Header.Comment))
        {
            diagnostics.Add($"Comment: {catalog.Header.Comment}");
        }

        var unsupported = new List<string>();

        var summary = new CathyImportSummary(
            Version: catalog.Header.Version,
            VolumeName: root.Name,
            TotalFolders: foldersById.Count,
            TotalFiles: totalFiles,
            TotalSizeBytes: totalSize,
            FreeSizeMb: catalog.Header.FreeSizeMb,
            IsArchive: catalog.Header.ArchiveFlag != 0,
            Diagnostics: diagnostics,
            UnsupportedFeatures: unsupported);

        return (flatList, summary);
    }
}
