using Couchbase.Lite;
using SkyCD.App.Services.Documents;
using SkyCD.Presentation.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.App.Services;

public sealed class CouchbaseLiteBrowserDataStore : IBrowserDataStore
{
    private const string RootKey = "__root__";
    private const string CatalogSeedDocumentId = "catalog-seed";
    private readonly Collection _catalogCollection;

    public CouchbaseLiteBrowserDataStore(CouchbaseLocalStore localStore)
    {
        _catalogCollection = localStore.GetCollection(LocalCollection.Catalog);
        EnsureSeedData();
    }

    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        using var seed = _catalogCollection.GetDocument(CatalogSeedDocumentId);
        var seedDocument = CatalogSeedDocument.FromDocument(seed);
        if (seedDocument is null)
        {
            return [];
        }

        var childrenByParent = seedDocument.TreeNodes
            .GroupBy(record => record.ParentKey ?? RootKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        return BuildTreeNodes(RootKey, childrenByParent);
    }

    public IReadOnlyList<BrowserItem> GetBrowserItems(string nodeKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey))
        {
            return [];
        }

        using var seed = _catalogCollection.GetDocument(CatalogSeedDocumentId);
        var seedDocument = CatalogSeedDocument.FromDocument(seed);
        if (seedDocument is null)
        {
            return [];
        }

        return seedDocument.BrowserItems
            .Where(item => nodeKey.Equals(item.NodeKey, StringComparison.OrdinalIgnoreCase))
            .Select(item => new BrowserItem(item.Name, item.Type, item.Size, item.IconGlyph))
            .ToArray();
    }

    private static IReadOnlyList<BrowserTreeNode> BuildTreeNodes(
        string parentKey,
        IReadOnlyDictionary<string, List<CatalogTreeNodeDocument>> childrenByParent)
    {
        if (!childrenByParent.TryGetValue(parentKey, out var children))
        {
            return [];
        }

        return children
            .Select(record => new BrowserTreeNode(
                record.Key,
                record.Title,
                record.IconGlyph,
                BuildTreeNodes(record.Key, childrenByParent),
                isExpanded: record.ParentKey is null))
            .ToArray();
    }

    private void EnsureSeedData()
    {
        if (_catalogCollection.GetDocument(CatalogSeedDocumentId) is not null)
        {
            return;
        }

        using var document = CatalogSeedDocument.CreateDefault().ToMutableDocument(CatalogSeedDocumentId);
        _catalogCollection.Save(document);
    }
}
