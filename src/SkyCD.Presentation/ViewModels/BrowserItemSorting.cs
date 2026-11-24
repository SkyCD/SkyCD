using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Orders browser entries the way the details headers and the Arrange menu ask for: folders stay on
/// top, then the active column decides the order, and equal values fall back to the name. Names use a
/// natural order so "Track 2" comes before "Track 10".
/// </summary>
internal static class BrowserItemSorting
{
    public static CatalogDocument[] Sort(IEnumerable<CatalogDocument> items, string? sortMode, bool descending)
    {
        ArgumentNullException.ThrowIfNull(items);
        var definition = DetailsColumnsCatalog.Find(sortMode) ?? DetailsColumnsCatalog.Find(DetailsColumnsCatalog.NameId);
        var comparer = CreateComparer(definition);
        var ranked = items.OrderBy(GetFolderRank);
        var ordered = descending
            ? ranked.ThenByDescending<CatalogDocument, CatalogDocument>(static item => item, comparer)
            : ranked.ThenBy<CatalogDocument, CatalogDocument>(static item => item, comparer);

        return ordered.ThenBy(static item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private static int GetFolderRank(CatalogDocument item) =>
        item.Type is CatalogDocumentType.Folder or CatalogDocumentType.NetworkFolder ? 0 : 1;

    private static IComparer<CatalogDocument> CreateComparer(DetailsColumnDefinition? definition)
    {
        var id = definition?.Id;
        if (id is null || string.Equals(id, DetailsColumnsCatalog.NameId, StringComparison.OrdinalIgnoreCase))
        {
            return NaturalNameComparer.Instance;
        }

        if (string.Equals(id, DetailsColumnsCatalog.SizeId, StringComparison.OrdinalIgnoreCase))
        {
            return Comparer<CatalogDocument>.Create(static (left, right) => left.Size.CompareTo(right.Size));
        }

        if (string.Equals(id, DetailsColumnsCatalog.TypeId, StringComparison.OrdinalIgnoreCase))
        {
            return Comparer<CatalogDocument>.Create(static (left, right) =>
                NaturalNameComparer.CompareStrings(left.DisplayType, right.DisplayType));
        }

        if (string.Equals(id, DetailsColumnsCatalog.StatusId, StringComparison.OrdinalIgnoreCase))
        {
            return Comparer<CatalogDocument>.Create(static (left, right) =>
                NaturalNameComparer.CompareStrings(left.Status, right.Status));
        }

        var valueSelector = definition!.ValueSelector;
        return Comparer<CatalogDocument>.Create((left, right) =>
            NaturalNameComparer.CompareStrings(valueSelector?.Invoke(left), valueSelector?.Invoke(right)));
    }
}
