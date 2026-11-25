using System;
using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.Catalog;

/// <summary>Neutral classification of a catalog entry, mapped from the host document type.</summary>
public enum CatalogEntryKind
{
    /// <summary>A directory entry.</summary>
    Folder,

    /// <summary>A file entry.</summary>
    File,

    /// <summary>A storage volume entry.</summary>
    Volume,

    /// <summary>An entry that does not fit the other kinds.</summary>
    Other
}

/// <summary>
/// A framework-neutral read model of one catalog entry. It is deliberately independent of the host
/// persistence type so feature plugins can read, decorate, project and search entries without
/// referencing <c>SkyCD.Documents</c>.
/// </summary>
public sealed record CatalogEntry(
    string Id,
    string Name,
    string Path,
    string? ParentId,
    CatalogEntryKind Kind,
    long Size,
    IReadOnlyDictionary<string, string> Properties);

/// <summary>Kind of catalog transition reported to a lifecycle-aware capability.</summary>
public enum CatalogLifecycle
{
    /// <summary>A catalog was opened and is now the active one.</summary>
    Opened,

    /// <summary>The active catalog was closed and no catalog remains open.</summary>
    Closed,

    /// <summary>The active catalog's contents or identity changed.</summary>
    Changed
}

/// <summary>
/// A per-entry visual decoration contributed by a feature plugin at projection time. The host renders
/// these on browser and tree entries without knowing what produced them.
/// </summary>
public sealed record EntryDecoration(
    string EntryId,
    string? BadgeText,
    string? BadgeIcon,
    string? Tooltip,
    bool Attention);

/// <summary>
/// A details-list column contributed by a plugin. <see cref="SelectValue"/> is invoked once per entry
/// during projection; a null result renders as blank. <see cref="Order"/> breaks ties across contributors.
/// </summary>
public sealed record DetailsColumnContribution(
    string Key,
    string Title,
    int Order,
    Func<CatalogEntry, string?> SelectValue);

/// <summary>
/// An advanced-search field contributed by a plugin. Presence-only fields (no <see cref="SelectValue"/>)
/// are evaluated through <see cref="Matches"/>; fields with a selector expose that entry text so the host
/// can apply the ordinary text operators to it. The host exposes the field to the filter UI keyed by <see cref="Key"/>.
/// </summary>
public sealed record AdvancedSearchFieldContribution(
    string Key,
    string Title,
    Func<CatalogEntry, bool> Matches,
    Func<CatalogEntry, string?>? SelectValue = null);

/// <summary>A per-entry command a plugin contributes to the browser/tree context menu for a single entry.</summary>
public sealed record EntryCommandContribution(string CommandId, string Title, int Order);
