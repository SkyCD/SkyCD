using System;
using System.Collections.Generic;
using System.Linq;
using Couchbase.Lite;
using Couchbase.Lite.Query;
using SkyCD.Couchbase.Mapping;
using SkyCD.Couchbase;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.ViewModels;
using SkyCD.Presentation.ViewModels.Catalog;
using CatalogEntryDocument = SkyCD.Documents.CatalogDocument;

namespace SkyCD.App.Services;

public sealed class CouchbaseLiteBrowserDataStore : IBrowserDataStore
{
    private readonly Collection _catalogCollection;
    private readonly CatalogDocumentRepository _catalogRepository;

    public CouchbaseLiteBrowserDataStore(DatabaseManager databaseManager, RepositoryManager repositoryManager)
    {
        _catalogRepository = repositoryManager.For<CatalogEntryDocument>() as CatalogDocumentRepository
            ?? throw new InvalidOperationException("Catalog document repository must be CatalogDocumentRepository.");
        var catalogRepository = _catalogRepository;
        var database = databaseManager.GetFor<CatalogEntryDocument>();
        catalogRepository.Collection = database.GetCollection(catalogRepository.CollectionName, Collection.DefaultScopeName)
                                     ?? database.CreateCollection(catalogRepository.CollectionName, Collection.DefaultScopeName);
        _catalogCollection = catalogRepository.Collection;
        EnsureSeedData();
    }

    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        var entries = LoadCatalogEntries();

        // Get all non-file entries for tree view
        var treeEntries = entries.Where(entry =>
            entry.Type != CatalogDocumentType.File)
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
                var type = MapCatalogEntryType(item.Type);
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

        return _catalogRepository.CreateDefaultEntries()
            .Where(item => string.Equals(item.ParentId, nodeKey, StringComparison.Ordinal))
            .Select(item =>
            {
                var type = MapCatalogEntryType(item.Type);
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
                var type = MapCatalogEntryType(entry.Type);
                return new BrowserTreeNode(
                    entry.Id,
                    entry.Name,
                    GetIconGlyph(type),
                    BuildTreeNodes(entry.Id, childrenByParent),
                    isExpanded: parentId == "__root__");
            })
            .ToArray();
    }

    private IReadOnlyList<BrowserTreeNode> BuildDefaultTreeNodes()
    {
        var entries = _catalogRepository.CreateDefaultEntries()
            .Where(entry => entry.Type != CatalogDocumentType.File)
            .ToList();

        var byParent = entries
            .GroupBy(record => record.ParentId ?? "__root__", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return BuildTreeNodes("__root__", byParent);
    }

    private static CatalogEntryType MapCatalogEntryType(CatalogDocumentType type)
    {
        return type switch
        {
            CatalogDocumentType.File => CatalogEntryType.File,
            CatalogDocumentType.Media => CatalogEntryType.Media,
            CatalogDocumentType.Folder => CatalogEntryType.Folder,
            CatalogDocumentType.NetworkResource => CatalogEntryType.NetworkResource,
            _ => CatalogEntryType.File
        };
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

        foreach (var entry in _catalogRepository.CreateDefaultEntries())
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
            : _catalogRepository.CreateDefaultEntries();
    }
}
