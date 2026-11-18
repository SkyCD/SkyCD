using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// One host-scheduled extraction pass for a catalog entry. The caller supplies a factory that returns a
/// fresh readable stream per provider, exactly as <see cref="SkyCD.Plugin.Host.Media.MediaExtensionManager"/>
/// expects; nothing in the cache layer needs a filesystem path.
/// </summary>
public sealed class MediaExtractionRequest
{
    public required string EntryId { get; init; }

    public required string Fingerprint { get; init; }

    public required MediaItem Item { get; init; }

    public required Func<CancellationToken, Task<Stream>> OpenSource { get; init; }

    /// <summary>
    /// An existing batch to stage into, normally the batch of the running scan so results are published
    /// together with the catalog entries. When null the scheduler opens and publishes its own batch.
    /// </summary>
    public string? BatchId { get; init; }

    /// <summary>Re-extract even when a current captured record already exists.</summary>
    public bool ForceRefresh { get; init; }

    /// <summary>
    /// Which matching providers this pass may execute. The default keeps whole-file hashing out of
    /// implicit extraction; adding files selects opt-in metadata providers separately.
    /// </summary>
    public SkyCD.Plugin.Host.Media.MediaProviderSelection ProviderSelection { get; init; } =
        SkyCD.Plugin.Host.Media.MediaProviderSelection.Standard;

    /// <summary>Builds a request from a catalog entry, deriving the fingerprint and source facts.</summary>
    public static MediaExtractionRequest ForCatalogEntry(
        CatalogDocument entry,
        Func<CancellationToken, Task<Stream>> openSource,
        IReadOnlyList<CatalogDocument>? catalog = null,
        string? batchId = null,
        bool forceRefresh = false,
        SkyCD.Plugin.Host.Media.MediaProviderSelection providerSelection =
            SkyCD.Plugin.Host.Media.MediaProviderSelection.Standard)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(openSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);

        return new MediaExtractionRequest
        {
            EntryId = entry.Id,
            Fingerprint = MediaSourceFingerprint.ForCatalogEntry(entry, catalog),
            Item = new MediaItem(MediaSourceFingerprint.GetExtension(entry), ReadContentType(entry), entry.Size),
            OpenSource = openSource,
            BatchId = batchId,
            ForceRefresh = forceRefresh,
            ProviderSelection = providerSelection
        };
    }

    private static string? ReadContentType(CatalogDocument entry)
    {
        if (!entry.Properties.TryGetValue("ContentType", out var value) || value is null)
        {
            return null;
        }

        var contentType = value.ToString();
        return string.IsNullOrWhiteSpace(contentType) ? null : contentType;
    }
}
