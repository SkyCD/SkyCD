using Couchbase.Lite;
using System.Collections.Generic;

namespace SkyCD.App.Services.Documents;

public sealed class CatalogSeedDocument
{
    public IReadOnlyList<CatalogTreeNodeDocument> TreeNodes { get; init; } = [];

    public IReadOnlyList<CatalogBrowserItemDocument> BrowserItems { get; init; } = [];

    public static CatalogSeedDocument CreateDefault()
    {
        return new CatalogSeedDocument
        {
            TreeNodes =
            [
                new CatalogTreeNodeDocument("library", null, "Library", "cd"),
                new CatalogTreeNodeDocument("movies", "library", "Movies", "folder"),
                new CatalogTreeNodeDocument("music", "library", "Music", "folder"),
                new CatalogTreeNodeDocument("projects", "library", "Projects", "folder")
            ],
            BrowserItems =
            [
                new CatalogBrowserItemDocument("library", "Movies", "Folder", "128 items", "folder"),
                new CatalogBrowserItemDocument("library", "Music", "Folder", "340 items", "folder"),
                new CatalogBrowserItemDocument("library", "Projects", "Folder", "56 items", "folder"),
                new CatalogBrowserItemDocument("movies", "Interstellar.mkv", "Video", "12.1 GB", "video"),
                new CatalogBrowserItemDocument("movies", "Arrival.mkv", "Video", "9.4 GB", "video"),
                new CatalogBrowserItemDocument("music", "Classical Collection", "Folder", "42 items", "folder"),
                new CatalogBrowserItemDocument("music", "Concert-2025.flac", "Audio", "414 MB", "audio"),
                new CatalogBrowserItemDocument("projects", "SkyCD v3", "Folder", "11 items", "folder"),
                new CatalogBrowserItemDocument("projects", "Plugin Benchmarks", "Folder", "6 items", "folder")
            ]
        };
    }

    public static CatalogSeedDocument? FromDocument(Document? document)
    {
        if (document is null)
        {
            return null;
        }

        var treeNodes = new List<CatalogTreeNodeDocument>();
        var treeNodesArray = document.GetArray("treeNodes");
        if (treeNodesArray is not null)
        {
            for (var index = 0; index < treeNodesArray.Count; index++)
            {
                var item = treeNodesArray.GetDictionary(index);
                if (item is null)
                {
                    continue;
                }

                var key = item.GetString("key");
                var title = item.GetString("title");
                var iconGlyph = item.GetString("iconGlyph");
                if (string.IsNullOrWhiteSpace(key) ||
                    string.IsNullOrWhiteSpace(title) ||
                    string.IsNullOrWhiteSpace(iconGlyph))
                {
                    continue;
                }

                treeNodes.Add(new CatalogTreeNodeDocument(
                    key,
                    item.GetString("parentKey"),
                    title,
                    iconGlyph));
            }
        }

        var browserItems = new List<CatalogBrowserItemDocument>();
        var browserItemsArray = document.GetArray("browserItems");
        if (browserItemsArray is not null)
        {
            for (var index = 0; index < browserItemsArray.Count; index++)
            {
                var item = browserItemsArray.GetDictionary(index);
                if (item is null)
                {
                    continue;
                }

                var nodeKey = item.GetString("nodeKey");
                var name = item.GetString("name");
                var type = item.GetString("type");
                var size = item.GetString("size");
                var iconGlyph = item.GetString("iconGlyph");
                if (string.IsNullOrWhiteSpace(nodeKey) ||
                    string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(type) ||
                    string.IsNullOrWhiteSpace(size) ||
                    string.IsNullOrWhiteSpace(iconGlyph))
                {
                    continue;
                }

                browserItems.Add(new CatalogBrowserItemDocument(
                    nodeKey,
                    name,
                    type,
                    size,
                    iconGlyph));
            }
        }

        return new CatalogSeedDocument
        {
            TreeNodes = treeNodes,
            BrowserItems = browserItems
        };
    }

    public MutableDocument ToMutableDocument(string documentId)
    {
        var treeNodes = new MutableArrayObject();
        foreach (var item in TreeNodes)
        {
            var node = new MutableDictionaryObject();
            node.SetString("key", item.Key);
            node.SetString("title", item.Title);
            node.SetString("iconGlyph", item.IconGlyph);
            if (!string.IsNullOrWhiteSpace(item.ParentKey))
            {
                node.SetString("parentKey", item.ParentKey);
            }

            treeNodes.AddDictionary(node);
        }

        var browserItems = new MutableArrayObject();
        foreach (var item in BrowserItems)
        {
            var browserItem = new MutableDictionaryObject();
            browserItem.SetString("nodeKey", item.NodeKey);
            browserItem.SetString("name", item.Name);
            browserItem.SetString("type", item.Type);
            browserItem.SetString("size", item.Size);
            browserItem.SetString("iconGlyph", item.IconGlyph);
            browserItems.AddDictionary(browserItem);
        }

        var document = new MutableDocument(documentId);
        document.SetArray("treeNodes", treeNodes);
        document.SetArray("browserItems", browserItems);
        return document;
    }
}

public sealed record CatalogTreeNodeDocument(string Key, string? ParentKey, string Title, string IconGlyph);

public sealed record CatalogBrowserItemDocument(string NodeKey, string Name, string Type, string Size, string IconGlyph);
