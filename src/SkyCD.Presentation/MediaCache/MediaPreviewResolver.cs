using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SkyCD.Documents;
using SkyCD.UI.Controls.Preview;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Resolves what the catalog browser can display for one entry using stored cache records only. It never
/// opens the original media and never invokes a plugin, so previews and typed metadata stay available
/// while a disc is disconnected and after the producing plugin is disabled or uninstalled.
/// </summary>
public sealed class MediaPreviewResolver
{
    private static readonly string[] TextContentTypes =
    [
        "text/plain",
        "text/markdown",
        "text/csv",
        "text/xml",
        "application/json",
        "application/xml"
    ];

    private readonly IMediaCacheStore store;
    private readonly TimeProvider timeProvider;

    public MediaPreviewResolver(
        IMediaCacheStore store,
        MediaPreviewDisplayPolicy? policy = null,
        TimeProvider? timeProvider = null)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        Policy = policy ?? new MediaPreviewDisplayPolicy();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The bounds applied while resolving and decoding.</summary>
    public MediaPreviewDisplayPolicy Policy { get; }

    /// <summary>The store this resolver reads from.</summary>
    public IMediaCacheStore Store => store;

    /// <summary>
    /// Builds the display snapshot of one entry. Captured results win over stale ones, a payload above the
    /// display bound is reported rather than decoded, and a record whose bytes are gone is reported as
    /// missing instead of failing navigation.
    /// </summary>
    /// <remarks>
    /// Synchronous and store-only by design: callers run it on the thread that owns the store so no
    /// background read can outlive a closed catalog database. Only decoding belongs off that thread.
    /// </remarks>
    /// <param name="entry">The selected entry, or null when nothing is selected.</param>
    /// <param name="markServed">
    /// Whether serving this entry should protect its record from LRU eviction. Browser thumbnails pass
    /// false, because realizing a row while scrolling is not the same as the user opening the item.
    /// </param>
    public EntryPreviewSnapshot Resolve(CatalogDocument? entry, bool markServed = true)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Id))
        {
            return EntryPreviewSnapshot.NoSelection;
        }

        var records = store.GetRecordsForEntry(entry.Id);
        var fields = MediaEntryMetadataProjection.Project(entry, records);

        if (records.Count == 0)
        {
            return new EntryPreviewSnapshot
            {
                EntryId = entry.Id,
                State = fields.Count > 0 ? MediaPreviewState.Ready : MediaPreviewState.Unavailable,
                Fields = fields
            };
        }

        var sawMissingArtifact = false;
        var sawOversizedArtifact = false;

        foreach (var record in OrderPreviewRecords(records))
        {
            var artifact = SelectArtifact(record);
            if (artifact is null)
            {
                continue;
            }

            if (artifact.Length > Policy.MaxDecodedArtifactBytes)
            {
                sawOversizedArtifact = true;
                continue;
            }

            if (record.State == MediaCacheRecordState.Missing)
            {
                sawMissingArtifact = true;
                continue;
            }

            var snapshot = BuildPayloadSnapshot(entry.Id, record, artifact, fields);
            if (markServed)
            {
                store.Touch(record.Id, timeProvider.GetUtcNow());
            }

            return snapshot;
        }

        return new EntryPreviewSnapshot
        {
            EntryId = entry.Id,
            State = ResolveFallbackState(records, fields, sawMissingArtifact, sawOversizedArtifact),
            FailureReason = ResolveFailureReason(records),
            ProviderId = ResolveProviderId(records),
            Fields = fields
        };
    }

    /// <summary>Reads the stored bytes of a resolved payload; false when the payload is unavailable.</summary>
    public bool TryReadPayload(EntryPreviewSnapshot snapshot, out byte[] data)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        data = [];

        if (snapshot.RecordId is null || snapshot.Artifact is null)
        {
            return false;
        }

        return store.TryReadArtifact(snapshot.RecordId, snapshot.Artifact.Key, out data!);
    }

    private EntryPreviewSnapshot BuildPayloadSnapshot(
        string entryId,
        MediaCacheRecord record,
        MediaArtifactReference artifact,
        IReadOnlyList<MediaMetadataFieldView> fields)
    {
        var kind = ClassifyPayload(artifact.ContentType);
        string? text = null;
        var truncated = false;

        if (kind == PreviewPayloadKind.Text && store.TryReadArtifact(record.Id, artifact.Key, out var bytes))
        {
            (text, truncated) = DecodeBoundedText(bytes, Policy.MaxTextPreviewCharacters);
        }

        return new EntryPreviewSnapshot
        {
            EntryId = entryId,
            State = record.State == MediaCacheRecordState.Captured
                ? MediaPreviewState.Ready
                : MediaPreviewState.Stale,
            PayloadKind = text is null && kind == PreviewPayloadKind.Text ? PreviewPayloadKind.None : kind,
            RecordId = record.Id,
            Artifact = artifact,
            ProviderId = record.PluginId,
            CapturedAtUtc = record.CapturedAtUtc,
            TextPreview = text,
            IsTextPreviewTruncated = truncated,
            Fields = fields
        };
    }

    private static MediaPreviewState ResolveFallbackState(
        IReadOnlyList<MediaCacheRecord> records,
        IReadOnlyList<MediaMetadataFieldView> fields,
        bool sawMissingArtifact,
        bool sawOversizedArtifact)
    {
        if (fields.Count > 0)
        {
            return fields.All(field => field.Origin == MediaMetadataOrigin.Captured && field.IsFromStaleRecord)
                ? MediaPreviewState.Stale
                : MediaPreviewState.Ready;
        }

        if (sawMissingArtifact || records.Any(record => record.State == MediaCacheRecordState.Missing))
        {
            return MediaPreviewState.MissingArtifact;
        }

        if (sawOversizedArtifact)
        {
            return MediaPreviewState.TooLarge;
        }

        if (records.Any(record => record.State == MediaCacheRecordState.Failed))
        {
            return MediaPreviewState.Failed;
        }

        if (records.Any(record => record.State == MediaCacheRecordState.Unsupported))
        {
            return MediaPreviewState.Unsupported;
        }

        return records.All(record => record.State == MediaCacheRecordState.Stale)
            ? MediaPreviewState.Stale
            : MediaPreviewState.Unavailable;
    }

    private static string? ResolveFailureReason(IReadOnlyList<MediaCacheRecord> records) => records
        .Where(record => record.State == MediaCacheRecordState.Failed && !string.IsNullOrWhiteSpace(record.FailureReason))
        .OrderByDescending(record => record.CapturedAtUtc)
        .Select(record => record.FailureReason)
        .FirstOrDefault();

    private static string? ResolveProviderId(IReadOnlyList<MediaCacheRecord> records) => records
        .OrderBy(StateRank)
        .ThenByDescending(record => record.Priority)
        .Select(record => record.PluginId)
        .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));

    /// <summary>Preview-bearing records first, current before superseded, highest priority provider first.</summary>
    private static IEnumerable<MediaCacheRecord> OrderPreviewRecords(IReadOnlyList<MediaCacheRecord> records) => records
        .Where(record => record.State is MediaCacheRecordState.Captured or MediaCacheRecordState.Stale
            or MediaCacheRecordState.Missing)
        .OrderBy(StateRank)
        .ThenByDescending(record => record.Priority)
        .ThenBy(record => record.PluginId, StringComparer.Ordinal)
        .ThenBy(record => record.Id, StringComparer.Ordinal);

    private static int StateRank(MediaCacheRecord record) => record.State switch
    {
        MediaCacheRecordState.Captured => 0,
        MediaCacheRecordState.Stale => 1,
        _ => 2
    };

    private static MediaArtifactReference? SelectArtifact(MediaCacheRecord record) => record.Artifacts
        .Where(artifact => !string.IsNullOrWhiteSpace(artifact.Key))
        .OrderBy(artifact => artifact.Key, StringComparer.Ordinal)
        .FirstOrDefault();

    private static PreviewPayloadKind ClassifyPayload(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return PreviewPayloadKind.Image;
        }

        var normalized = contentType.Trim();
        if (normalized.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            TextContentTypes.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return PreviewPayloadKind.Text;
        }

        return PreviewPayloadKind.Image;
    }

    private static (string Text, bool Truncated) DecodeBoundedText(byte[] bytes, int maxCharacters)
    {
        if (bytes.Length == 0)
        {
            return (string.Empty, false);
        }

        // Invalid bytes are replaced rather than thrown: a truncated capture must still be readable.
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false)
            .GetString(bytes);

        if (text.Length <= maxCharacters)
        {
            return (text, false);
        }

        return (text[..maxCharacters], true);
    }
}
