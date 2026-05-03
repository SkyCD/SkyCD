using Couchbase.Lite;
using Couchbase.Lite.Query;
using SkyCD.Couchbase.Mapping;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.Presentation.ViewModels.Catalog;
using System;
using System.Collections.Generic;
using System.Linq;
using CatalogEntryDocument = SkyCD.Documents.CatalogDocument;

namespace SkyCD.App.Services;

public sealed class CouchbaseLiteBrowserDataStore : IBrowserDataStore
{
    private readonly Collection _catalogCollection;

    public CouchbaseLiteBrowserDataStore(CouchbaseLocalStore localStore)
    {
        var catalogRepository = localStore.GetRepository<CatalogEntryDocument>();
        _catalogCollection = catalogRepository.Collection;
        EnsureSeedData();
    }

    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        var entries = LoadCatalogEntries();

        // Get all non-file entries for tree view
        var treeEntries = entries.Where(entry =>
            !string.Equals(entry.Type, "File", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var childrenByParent = treeEntries
            .GroupBy(record => record.ParentId ?? "__root__", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var roots = BuildTreeNodes("__root__", childrenByParent);
        if (roots.Count > 0)
        {
            return roots;
        }

        return BuildDefaultTreeNodes();
    }

    public IReadOnlyList<BrowserItem> GetBrowserItems(string nodeKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey))
        {
            return [];
        }

        var entries = LoadCatalogEntries();

        var items = entries
            .Where(item => string.Equals(item.ParentId, nodeKey, StringComparison.Ordinal))
            .Select(item =>
            {
                var type = ParseCatalogEntryType(item.Type);
                return new BrowserItem(
                    item.Name,
                    GetItemTypeDisplayName(type),
                    FormatSize(item.Size),
                    GetIconGlyph(type));
            })
            .ToArray();

        if (items.Length > 0)
        {
            return items;
        }

        return SkyCD.Documents.CatalogDocument.CreateDefaultEntries()
            .Where(item => string.Equals(item.ParentId, nodeKey, StringComparison.Ordinal))
            .Select(item =>
            {
                var type = ParseCatalogEntryType(item.Type);
                return new BrowserItem(
                    item.Name,
                    GetItemTypeDisplayName(type),
                    FormatSize(item.Size),
                    GetIconGlyph(type));
            })
            .ToArray();
    }

    private static IReadOnlyList<BrowserTreeNode> BuildTreeNodes(
        string parentId,
        IReadOnlyDictionary<string, List<CatalogEntryDocument>> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(parentId, out var children))
        {
            return [];
        }

        return children
            .Select(entry =>
            {
                var type = ParseCatalogEntryType(entry.Type);
                return new BrowserTreeNode(
                    entry.Id,
                    entry.Name,
                    GetIconGlyph(type),
                    BuildTreeNodes(entry.Id, childrenByParent),
                    isExpanded: parentId == "__root__");
            })
            .ToArray();
    }

    private static IReadOnlyList<BrowserTreeNode> BuildDefaultTreeNodes()
    {
        var entries = SkyCD.Documents.CatalogDocument.CreateDefaultEntries()
            .Where(entry => !string.Equals(entry.Type, "File", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var byParent = entries
            .GroupBy(record => record.ParentId ?? "__root__", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return BuildTreeNodes("__root__", byParent);
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
        if (LoadCatalogEntries().Count > 0)
        {
            return;
        }

        foreach (var entry in SkyCD.Documents.CatalogDocument.CreateDefaultEntries())
        {
            using var document = entry.ToMutableDocument(entry.Id);
            _catalogCollection.Save(document);
        }
    }

    private IReadOnlyList<CatalogEntryDocument> LoadCatalogEntries()
    {
        using var query = QueryBuilder
            .Select(SelectResult.All())
            .From(DataSource.Collection(_catalogCollection));

        using var results = query.Execute();
        var entries = new List<CatalogEntryDocument>();

        foreach (var row in results)
        {
            var dictionary = row.GetDictionary(_catalogCollection.Name);
            if (dictionary is null)
            {
                continue;
            }

            using var document = new MutableDocument(dictionary.ToDictionary());
            var mapped = document.FromDocument<CatalogEntryDocument>();
            if (mapped is not null)
            {
                entries.Add(mapped);
            }
        }

        return entries.Count > 0
            ? entries
            : SkyCD.Documents.CatalogDocument.CreateDefaultEntries();
    }
}
