using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>An audio CD root found among scanned documents and the entry that represents it, when present.</summary>
public sealed record AudioCdDiscRoot(string RootPath, CatalogDocument? RootDocument);

/// <summary>
/// Captures audio CD discs during a scan and stores the disc and per-track results in the media cache.
/// Discs are recognised from the scanned root (an optical drive) or from the virtual <c>.cda</c> track
/// entries, and matched track records reuse those entries so their details stay readable offline.
/// </summary>
public sealed class AudioCdMetadataService
{
    private const string RootDirectoryPropertyKey = "RootDirectory";
    private const string DriveTypePropertyKey = "DriveType";
    private const string TrackExtension = ".cda";

    private readonly IAudioCdMetadataProvider provider;
    private readonly IMediaCacheStore store;
    private readonly TimeProvider timeProvider;

    public AudioCdMetadataService(IAudioCdMetadataProvider provider, IMediaCacheStore store,
        TimeProvider? timeProvider = null)
    {
        this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool CanCollect => provider.HasProviders;

    /// <summary>
    /// Captures every audio CD root among the published documents. Returns the number of cache records staged
    /// into <paramref name="batchId"/>; an unmatched, offline, or unreachable disc stages nothing.
    /// </summary>
    public async Task<int> CollectAsync(IReadOnlyList<CatalogDocument> documents,
        IReadOnlyList<CatalogDocument> catalog, string batchId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);

        var staged = 0;
        foreach (var root in FindDiscRoots(documents))
        {
            cancellationToken.ThrowIfCancellationRequested();

            AudioCdProviderCapture? captured;
            try
            {
                captured = await provider.CaptureAsync(root.RootPath, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // A broken provider must never fail the scan; other roots are still attempted.
                continue;
            }

            if (captured is null)
            {
                continue;
            }

            staged += StageDisc(root, captured, catalog, batchId);
            staged += StageTracks(root, captured, documents, catalog, batchId);
        }

        return staged;
    }

    /// <summary>Finds optical drive roots and roots that already expose virtual <c>.cda</c> tracks.</summary>
    public static IReadOnlyList<AudioCdDiscRoot> FindDiscRoots(IReadOnlyList<CatalogDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var roots = new Dictionary<string, AudioCdDiscRoot>(StringComparer.OrdinalIgnoreCase);

        foreach (var document in documents)
        {
            if (document.Type == CatalogDocumentType.File)
            {
                continue;
            }

            var rootPath = ReadProperty(document.Properties, RootDirectoryPropertyKey)?.Trim();
            if (string.IsNullOrEmpty(rootPath) || !IsOpticalDrive(document))
            {
                continue;
            }

            roots[RootKey(rootPath)] = new AudioCdDiscRoot(rootPath, document);
        }

        foreach (var document in documents)
        {
            if (document.Type != CatalogDocumentType.File ||
                !string.Equals(Path.GetExtension(document.Name), TrackExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullName = ReadProperty(document.Properties, MediaMetadataService.SourcePathPropertyKey);
            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            // The disc root is the directory holding the virtual track file. Deriving it from the path keeps the
            // inference identical on Windows ("E:\") and on Unix, where a disc root can be a device path.
            var rootPath = Path.GetDirectoryName(fullName);
            var rootKey = rootPath is null ? null : RootKey(rootPath);
            if (rootKey is null || roots.ContainsKey(rootKey))
            {
                continue;
            }

            roots[rootKey] = new AudioCdDiscRoot(rootPath!, FindContainer(documents, document.ParentId));
        }

        return roots.Values.ToList();
    }

    private int StageDisc(AudioCdDiscRoot root, AudioCdProviderCapture captured,
        IReadOnlyList<CatalogDocument> catalog, string batchId)
    {
        if (root.RootDocument is null)
        {
            return 0;
        }

        var fingerprint = MediaSourceFingerprint.ForCatalogEntry(root.RootDocument, catalog);
        store.Stage(batchId, BuildPayload(root.RootDocument.Id, fingerprint, captured, captured.Capture.DiscSchema,
            captured.Capture.DiscValues, captured.Capture.DiscCover));
        return 1;
    }

    private int StageTracks(AudioCdDiscRoot root, AudioCdProviderCapture captured,
        IReadOnlyList<CatalogDocument> documents, IReadOnlyList<CatalogDocument> catalog, string batchId)
    {
        if (captured.Capture.Tracks.Count == 0)
        {
            return 0;
        }

        var byNumber = new Dictionary<int, CatalogDocument>();
        foreach (var document in documents)
        {
            if (document.Type != CatalogDocumentType.File ||
                !string.Equals(Path.GetExtension(document.Name), TrackExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullName = ReadProperty(document.Properties, MediaMetadataService.SourcePathPropertyKey);
            if (!IsDirectlyUnder(fullName, root.RootPath))
            {
                continue;
            }

            if (TryReadTrackNumber(document.Name, out var number))
            {
                byNumber.TryAdd(number, document);
            }
        }

        var staged = 0;
        foreach (var track in captured.Capture.Tracks)
        {
            if (!byNumber.TryGetValue(track.Number, out var document))
            {
                continue;
            }

            var fingerprint = MediaSourceFingerprint.ForCatalogEntry(document, catalog);
            store.Stage(batchId, BuildPayload(document.Id, fingerprint, captured, captured.Capture.TrackSchema,
                track.Values, track.Cover));
            staged++;
        }

        return staged;
    }

    private MediaCachePayload BuildPayload(string entryId, string fingerprint, AudioCdProviderCapture captured,
        MediaMetadataSchema schema, IReadOnlyList<MediaMetadataValue> values, MediaPreviewArtifact? cover)
    {
        var now = timeProvider.GetUtcNow();
        var record = new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateId(fingerprint, captured.PluginId, captured.PluginVersion,
                captured.Capability, schema.Id, schema.Version),
            EntryId = entryId,
            Fingerprint = fingerprint,
            PluginId = captured.PluginId,
            PluginVersion = captured.PluginVersion,
            Capability = captured.Capability,
            SchemaId = schema.Id,
            SchemaVersion = schema.Version,
            Priority = 0,
            State = MediaCacheRecordState.Captured,
            CapturedAtUtc = now,
            LastUsedAtUtc = now,
            SourceLength = 0,
            Metadata = values.Select(MediaMetadataValueRecord.From).ToList(),
            SchemaFields = schema.Fields.Select(MediaMetadataFieldRecord.From).ToList()
        };

        Dictionary<string, byte[]>? artifacts = null;
        if (cover is not null && !cover.Data.IsEmpty)
        {
            var bytes = cover.Data.ToArray();
            const string key = "preview:0";
            record.Artifacts.Add(new MediaArtifactReference
            {
                Key = key,
                ContentType = cover.ContentType,
                Length = bytes.Length,
                Sha256 = MediaCacheHashing.Sha256Hex(bytes)
            });
            artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [key] = bytes };
        }

        record.WithRecalculatedSizes();
        return new MediaCachePayload(record, artifacts);
    }

    private static CatalogDocument? FindContainer(IReadOnlyList<CatalogDocument> documents, string? parentId)
    {
        if (string.IsNullOrWhiteSpace(parentId))
        {
            return null;
        }

        return documents.FirstOrDefault(document =>
            document.Type != CatalogDocumentType.File && string.Equals(document.Id, parentId, StringComparison.Ordinal));
    }

    private static bool IsOpticalDrive(CatalogDocument document) =>
        string.Equals(ReadProperty(document.Properties, DriveTypePropertyKey), "CDRom",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectlyUnder(string? fullName, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(fullName);
        return directory is not null &&
               string.Equals(Path.TrimEndingDirectorySeparator(directory), Path.TrimEndingDirectorySeparator(rootPath),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string RootKey(string path) => Path.TrimEndingDirectorySeparator(path.Trim());

    private static bool TryReadTrackNumber(string fileName, out int number)
    {
        number = 0;
        var end = fileName.Length - TrackExtension.Length;
        var start = end;
        while (start > 0 && char.IsDigit(fileName[start - 1]))
        {
            start--;
        }

        return start < end && int.TryParse(fileName.AsSpan(start, end - start), NumberStyles.None,
            CultureInfo.InvariantCulture, out number);
    }

    private static string? ReadProperty(PropertiesCollection properties, string key) =>
        properties.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;
}
