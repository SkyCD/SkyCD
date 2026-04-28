using Couchbase.Lite;
using SkyCD.App.Services.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.Presentation.ViewModels.Catalog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.App.Services;

public sealed class CouchbaseLiteBrowserDataStore : IBrowserDataStore
{
    private const string CatalogDocumentId = "catalog";
    private readonly Collection _catalogCollection;

    public CouchbaseLiteBrowserDataStore(CouchbaseLocalStore localStore)
    {
        _catalogCollection = localStore.GetCollection(LocalCollection.Catalog);
        EnsureSeedData();
    }

    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        using var doc = _catalogCollection.GetDocument(CatalogDocumentId);
        var catalogDocument = SkyCD.App.Services.Documents.CatalogDocument.FromDocument(doc);
        if (catalogDocument is null)
        {
            return [];
        }

        // Get all non-file entries for tree view
        var treeEntries = catalogDocument.Entries.Where(entry => 
            !string.Equals(entry["Type"] as string, "File", StringComparison.OrdinalIgnoreCase))
            .ToList();
        
        var childrenByParent = treeEntries
            .GroupBy(record => record["ParentId"] as string ?? "__root__", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return BuildTreeNodes("__root__", childrenByParent);
    }

    public IReadOnlyList<BrowserItem> GetBrowserItems(string nodeKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey))
        {
            return [];
        }

        using var doc = _catalogCollection.GetDocument(CatalogDocumentId);
        var catalogDocument = SkyCD.App.Services.Documents.CatalogDocument.FromDocument(doc);
        if (catalogDocument is null)
        {
            return [];
        }

        return catalogDocument.Entries
            .Where(item => item["ParentId"] as string == nodeKey)
            .Select(item => 
            {
                var type = ParseCatalogEntryType(item["Type"] as string);
                var size = Convert.ToInt64(item["Size"]);
                return new BrowserItem(
                    item["Name"] as string ?? string.Empty,
                    GetItemTypeDisplayName(type),
                    FormatSize(size),
                    GetIconGlyph(type));
            })
            .ToArray();
    }

    private static IReadOnlyList<BrowserTreeNode> BuildTreeNodes(
        string parentId,
        IReadOnlyDictionary<string, List<Dictionary<string, object>>> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(parentId, out var children))
        {
            return [];
        }

        return children
            .Select(entry => 
            {
                var type = ParseCatalogEntryType(entry["Type"] as string);
                return new BrowserTreeNode(
                    entry["Id"] as string ?? string.Empty,
                    entry["Name"] as string ?? string.Empty,
                    GetIconGlyph(type),
                    BuildTreeNodes(entry["Id"] as string ?? string.Empty, childrenByParent),
                    isExpanded: parentId == "__root__");
            })
            .ToArray();
    }

    private static CatalogEntryType ParseCatalogEntryType(string? typeStr)
    {
        if (string.IsNullOrWhiteSpace(typeStr))
        {
            return CatalogEntryType.File;
        }

        return Enum.TryParse<CatalogEntryType>(typeStr, true, out var type) 
            ? type 
            : CatalogEntryType.File;
    }

    private static string GetItemTypeDisplayName(CatalogEntryType type)
    {
        return type switch
        {
            CatalogEntryType.File => "File",
            CatalogEntryType.Media => "Media",
            CatalogEntryType.Folder => "Folder",
            CatalogEntryType.NetworkResource => "Network Resource",
            _ => "Unknown"
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes == 0)
        {
            return "0 Bytes";
        }

        string[] sizes = ["Bytes", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    private static string GetIconGlyph(CatalogEntryType type)
    {
        return type switch
        {
            CatalogEntryType.File => "file",
            CatalogEntryType.Media => "video",
            CatalogEntryType.Folder => "folder",
            CatalogEntryType.NetworkResource => "network",
            _ => "file"
        };
    }

    private void EnsureSeedData()
    {
        if (_catalogCollection.GetDocument(CatalogDocumentId) is not null)
        {
            return;
        }

        using var document = SkyCD.App.Services.Documents.CatalogDocument.CreateDefault().ToMutableDocument(CatalogDocumentId);
        _catalogCollection.Save(document);
    }
}
