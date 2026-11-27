using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;

namespace SkyCD.SDK.Capabilities.Catalog;

/// <summary>
/// Host-provided save surface for plugins that write a subset of the open catalog in a loaded file format.
/// The host keeps the same payload snapshot, data-loss assessment and staging-then-rename write that Save As
/// uses, so a plugin never binds to a concrete catalog format and every written file opens again.
/// </summary>
public interface ICatalogSaveHostApi
{
    /// <summary>
    /// Gets the writable formats whose output the host can read back, ordered by display name. Formats that
    /// can only be written (exporters) are excluded, so a file written in one of them reopens as a catalog.
    /// </summary>
    IReadOnlyList<FileFormatDescriptor> GetRewritableFormats();

    /// <summary>
    /// Gets the rewritable format id that matches <paramref name="path"/>, or the first rewritable format
    /// when no loaded format matches. Returns an empty string when no rewritable format is loaded.
    /// </summary>
    string ResolveRewritableFormatId(string? path);

    /// <summary>Gets catalog-wide data outside the entry tree that the loaded formats may not store.</summary>
    IReadOnlyList<FileFormatDataLossItem> CollectCatalogScopeItems();

    /// <summary>
    /// Reports which catalog data a write of <paramref name="entries"/> in <paramref name="formatId"/> would
    /// drop. Entries must be the ones the host projected for the open catalog.
    /// </summary>
    IReadOnlyList<FileFormatDataLossItem> AssessDataLoss(
        string formatId,
        string fileName,
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<FileFormatDataLossItem> catalogScopeItems);

    /// <summary>
    /// Serializes <paramref name="entries"/> into <paramref name="target"/> without touching disk, so a
    /// caller can measure the size a write would produce.
    /// </summary>
    Task WriteAsync(
        string formatId,
        string fileName,
        Stream target,
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<FileFormatDataLossItem> catalogScopeItems,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="entries"/> to <paramref name="path"/> through the host staging-and-rename
    /// path, so an interrupted write never leaves a truncated catalog behind.
    /// </summary>
    Task WriteFileAsync(
        string formatId,
        string path,
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<FileFormatDataLossItem> catalogScopeItems,
        CancellationToken cancellationToken = default);
}
