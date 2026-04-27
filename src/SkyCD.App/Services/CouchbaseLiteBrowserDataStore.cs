using Couchbase.Lite;
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
        _catalogCollection = localStore.GetCollection(CouchbaseLocalStore.LocalCollection.Catalog);
        EnsureSeedData();
    }

    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        var records = new List<TreeNodeRecord>();
        using var seedDocument = _catalogCollection.GetDocument(CatalogSeedDocumentId);
        var treeNodes = seedDocument?.GetArray("treeNodes");
        if (treeNodes is null)
        {
            return [];
        }

        for (var index = 0; index < treeNodes.Count; index++)
        {
            var node = treeNodes.GetDictionary(index);
            if (node is null)
            {
                continue;
            }

            var key = node.GetString("key");
            var title = node.GetString("title");
            var iconGlyph = node.GetString("iconGlyph");

            if (string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(iconGlyph))
            {
                continue;
            }

            records.Add(new TreeNodeRecord(
                key,
                node.GetString("parentKey"),
                title,
                iconGlyph));
        }

        var childrenByParent = records
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

        using var seedDocument = _catalogCollection.GetDocument(CatalogSeedDocumentId);
        var items = seedDocument?.GetArray("browserItems");
        if (items is null)
        {
            return [];
        }

        var results = new List<BrowserItem>();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items.GetDictionary(index);
            if (item is null)
            {
                continue;
            }

            if (!nodeKey.Equals(item.GetString("nodeKey"), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = item.GetString("name");
            var type = item.GetString("type");
            var size = item.GetString("size");
            var iconGlyph = item.GetString("iconGlyph");

            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(type) ||
                string.IsNullOrWhiteSpace(size) ||
                string.IsNullOrWhiteSpace(iconGlyph))
            {
                continue;
            }

            results.Add(new BrowserItem(name, type, size, iconGlyph));
        }

        return results;
    }

    private static IReadOnlyList<BrowserTreeNode> BuildTreeNodes(
        string parentKey,
        IReadOnlyDictionary<string, List<TreeNodeRecord>> childrenByParent)
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

        var treeNodes = new MutableArrayObject();
        AddTreeNode(treeNodes, "library", null, "Library", "cd");
        AddTreeNode(treeNodes, "movies", "library", "Movies", "folder");
        AddTreeNode(treeNodes, "music", "library", "Music", "folder");
        AddTreeNode(treeNodes, "projects", "library", "Projects", "folder");

        var browserItems = new MutableArrayObject();
        AddBrowserItem(browserItems, "library", "Movies", "Folder", "128 items", "folder");
        AddBrowserItem(browserItems, "library", "Music", "Folder", "340 items", "folder");
        AddBrowserItem(browserItems, "library", "Projects", "Folder", "56 items", "folder");
        AddBrowserItem(browserItems, "movies", "Interstellar.mkv", "Video", "12.1 GB", "video");
        AddBrowserItem(browserItems, "movies", "Arrival.mkv", "Video", "9.4 GB", "video");
        AddBrowserItem(browserItems, "music", "Classical Collection", "Folder", "42 items", "folder");
        AddBrowserItem(browserItems, "music", "Concert-2025.flac", "Audio", "414 MB", "audio");
        AddBrowserItem(browserItems, "projects", "SkyCD v3", "Folder", "11 items", "folder");
        AddBrowserItem(browserItems, "projects", "Plugin Benchmarks", "Folder", "6 items", "folder");

        using var document = new MutableDocument(CatalogSeedDocumentId);
        document.SetArray("treeNodes", treeNodes)
            .SetArray("browserItems", browserItems);
        _catalogCollection.Save(document);
    }

    private static void AddTreeNode(
        MutableArrayObject array,
        string key,
        string? parentKey,
        string title,
        string iconGlyph)
    {
        var item = new MutableDictionaryObject();
        item.SetString("key", key);
        item.SetString("title", title);
        item.SetString("iconGlyph", iconGlyph);
        if (!string.IsNullOrWhiteSpace(parentKey))
        {
            item.SetString("parentKey", parentKey);
        }

        array.AddDictionary(item);
    }

    private static void AddBrowserItem(
        MutableArrayObject array,
        string nodeKey,
        string name,
        string type,
        string size,
        string iconGlyph)
    {
        var item = new MutableDictionaryObject();
        item.SetString("nodeKey", nodeKey);
        item.SetString("name", name);
        item.SetString("type", type);
        item.SetString("size", size);
        item.SetString("iconGlyph", iconGlyph);
        array.AddDictionary(item);
    }

    private sealed record TreeNodeRecord(string Key, string? ParentKey, string Title, string IconGlyph);
}
