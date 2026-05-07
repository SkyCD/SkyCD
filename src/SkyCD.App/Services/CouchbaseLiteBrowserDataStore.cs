using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Formatting;
using SkyCD.Presentation.ViewModels;
using CatalogEntryDocument = SkyCD.Documents.CatalogDocument;

namespace SkyCD.App.Services;

public sealed class CouchbaseLiteBrowserDataStore : IBrowserDataStore
{
    private readonly CatalogDocumentRepository catalogRepository;

    public CouchbaseLiteBrowserDataStore(RepositoryManager repositoryManager)
    {
        catalogRepository = repositoryManager.For<CatalogEntryDocument>() as CatalogDocumentRepository
            ?? throw new InvalidOperationException("Catalog document repository must be CatalogDocumentRepository.");
        EnsureSeedData();
    }

    public IReadOnlyList<BrowserTreeNode> GetTreeNodes()
    {
        var roots = catalogRepository
            .GetRoots<CatalogEntryDocument>()
            .Where(entry => entry.Type != CatalogDocumentType.File)
            .ToArray();

        var treeNodes = roots
            .Select(root =>
            {
                var descendants = catalogRepository
                    .GetDescendantsOf<CatalogEntryDocument>(root.Id)
                    .Where(entry => entry.Type != CatalogDocumentType.File)
                    .ToList();
                descendants.Add(root);

                var byParent = descendants
                    .GroupBy(entry => entry.ParentId ?? "__root__", StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

                return BuildTreeNodeFromLookup(root, byParent, isExpanded: true);
            })
            .ToArray();

        if (treeNodes.Length > 0)
        {
            return treeNodes;
        }

        return BuildDefaultTreeNodes();
    }

    public IReadOnlyList<BrowserItem> GetBrowserItems(string nodeKey)
    {
        if (string.IsNullOrWhiteSpace(nodeKey))
        {
            return [];
        }

        var entries = catalogRepository.GetChildrenOf<CatalogEntryDocument>(nodeKey);

        var items = entries
            .Select(item =>
            {
                return new BrowserItem(
                    item.Name,
                    item.Type.ToDisplayName(),
                    SizeFormatting.FormatBytes(item.Size, "0.##"),
                    item.Type.ResolveIconGlyph());
            })
            .ToArray();

        if (items.Length > 0)
        {
            return items;
        }

        return catalogRepository.CreateDefaultEntries()
            .Where(item => string.Equals(item.ParentId, nodeKey, StringComparison.Ordinal))
            .Select(item =>
            {
                return new BrowserItem(
                    item.Name,
                    item.Type.ToDisplayName(),
                    SizeFormatting.FormatBytes(item.Size, "0.##"),
                    item.Type.ResolveIconGlyph());
            })
            .ToArray();
    }

    private static BrowserTreeNode BuildTreeNodeFromLookup(
        CatalogEntryDocument entry,
        IReadOnlyDictionary<string, List<CatalogEntryDocument>> byParent,
        bool isExpanded)
    {
        byParent.TryGetValue(entry.Id, out var childrenOfCurrent);

        var children = (childrenOfCurrent ?? [])
            .Select(child => BuildTreeNodeFromLookup(child, byParent, isExpanded: false))
            .ToArray();

        return new BrowserTreeNode(
            entry.Id,
            entry.Name,
            entry.Type.ResolveIconGlyph(),
            children,
            isExpanded);
    }

    private IReadOnlyList<BrowserTreeNode> BuildDefaultTreeNodes()
    {
        var entries = catalogRepository.CreateDefaultEntries()
            .Where(entry => entry.Type != CatalogDocumentType.File)
            .ToList();
        var byId = entries.ToDictionary(entry => entry.Id, StringComparer.Ordinal);

        return entries
            .Where(entry => string.IsNullOrWhiteSpace(entry.ParentId))
            .Select(entry => BuildDefaultTreeNode(entry, byId, isExpanded: true))
            .ToArray();
    }

    private static BrowserTreeNode BuildDefaultTreeNode(
        CatalogEntryDocument entry,
        IReadOnlyDictionary<string, CatalogEntryDocument> byId,
        bool isExpanded)
    {
        var children = byId.Values
            .Where(candidate => string.Equals(candidate.ParentId, entry.Id, StringComparison.Ordinal))
            .Select(child => BuildDefaultTreeNode(child, byId, isExpanded: false))
            .ToArray();

        return new BrowserTreeNode(
            entry.Id,
            entry.Name,
            entry.Type.ResolveIconGlyph(),
            children,
            isExpanded);
    }

    private void EnsureSeedData()
    {
        if (catalogRepository.GetAll<CatalogEntryDocument>().Count > 0)
        {
            return;
        }

        foreach (var entry in catalogRepository.CreateDefaultEntries())
        {
            catalogRepository.Save(entry.Id, entry);
        }
    }
}
