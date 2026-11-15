using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Presentation.MediaCache.Exceptions;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Portable bundle form of the media cache: a JSON manifest plus content-addressed artifact files. It is
/// what a native catalog export or backup carries so that previews and captured metadata survive moving a
/// catalog to another machine, and it is the recovery source when stored artifacts go missing.
/// </summary>
/// <remarks>
/// Artifact files are named by their SHA-256 digest, so identical payloads are written once and a restored
/// bundle can be verified without the original media. Records in the bundle that are older than the record
/// already stored never overwrite it.
/// </remarks>
public sealed class MediaCacheBundle
{
    public const int FormatVersion = 1;
    public const string ManifestFileName = "manifest.json";
    public const string ArtifactsDirectoryName = "artifacts";
    private const string ArtifactFileExtension = ".bin";

    private static readonly JsonSerializerOptions ManifestOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IMediaCacheStore store;

    public MediaCacheBundle(IMediaCacheStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Writes every record and all verifiable artifact payloads into a bundle directory.</summary>
    public async Task<MediaCacheBundleExportResult> ExportAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var artifactsDirectory = Path.Combine(directory, ArtifactsDirectoryName);
        Directory.CreateDirectory(artifactsDirectory);

        var records = store.GetAllRecords();
        var exportedArtifacts = 0;
        var skippedArtifacts = 0;
        long artifactBytes = 0;

        foreach (var record in records)
        {
            foreach (var reference in record.Artifacts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var path = GetArtifactPath(artifactsDirectory, reference);
                if (File.Exists(path))
                {
                    exportedArtifacts++;
                    artifactBytes += reference.Length;
                    continue;
                }

                if (!TryReadVerifiedArtifact(record.Id, reference, out var data))
                {
                    skippedArtifacts++;
                    continue;
                }

                await File.WriteAllBytesAsync(path, data, cancellationToken).ConfigureAwait(false);
                exportedArtifacts++;
                artifactBytes += data.Length;
            }
        }

        var manifest = new MediaCacheBundleManifest
        {
            FormatVersion = FormatVersion,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            Records = [.. records]
        };

        var manifestPath = Path.Combine(directory, ManifestFileName);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestOptions),
            cancellationToken).ConfigureAwait(false);

        return new MediaCacheBundleExportResult(
            directory,
            records.Count,
            exportedArtifacts,
            skippedArtifacts,
            artifactBytes);
    }

    /// <summary>
    /// Restores records and artifacts from a bundle. Payloads that are absent or fail digest verification
    /// leave their record in the missing state so a later refresh can recover them.
    /// </summary>
    public async Task<MediaCacheBundleImportResult> ImportAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var manifestPath = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            throw new MediaCacheBundleManifestNotFoundException(manifestPath);
        }

        var manifest = JsonSerializer.Deserialize<MediaCacheBundleManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false),
            ManifestOptions);

        if (manifest is null)
        {
            throw new MediaCacheBundleManifestNotFoundException(manifestPath);
        }

        if (manifest.FormatVersion > FormatVersion)
        {
            throw new MediaCacheBundleFormatException(manifest.FormatVersion, FormatVersion);
        }

        var artifactsDirectory = Path.Combine(directory, ArtifactsDirectoryName);
        var imported = 0;
        var skipped = 0;
        var restoredArtifacts = 0;
        var missingArtifacts = 0;
        var incompleteRecords = 0;

        foreach (var bundled in manifest.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(bundled.Id) || string.IsNullOrWhiteSpace(bundled.EntryId))
            {
                skipped++;
                continue;
            }

            if (IsNewerRecordStored(bundled))
            {
                skipped++;
                continue;
            }

            var record = bundled.Clone();
            var artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            foreach (var reference in record.Artifacts)
            {
                var path = GetArtifactPath(artifactsDirectory, reference);
                if (!File.Exists(path))
                {
                    missingArtifacts++;
                    continue;
                }

                var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                if (data.LongLength != reference.Length ||
                    !string.Equals(MediaCacheHashing.Sha256Hex(data), reference.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    missingArtifacts++;
                    continue;
                }

                artifacts[reference.Key] = data;
                restoredArtifacts++;
            }

            if (artifacts.Count < record.Artifacts.Count)
            {
                record.State = MediaCacheRecordState.Missing;
                incompleteRecords++;
            }

            try
            {
                store.Save(new MediaCachePayload(record, artifacts));
                imported++;
            }
            catch (MediaCacheArtifactTooLargeException)
            {
                // The bundle came from a cache with looser bounds; keep the rest of the import usable.
                skipped++;
            }
        }

        return new MediaCacheBundleImportResult(
            imported,
            skipped,
            restoredArtifacts,
            missingArtifacts,
            incompleteRecords);
    }

    private bool IsNewerRecordStored(MediaCacheRecord bundled)
    {
        var stored = store.GetRecord(bundled.Id);
        return stored is not null && stored.CapturedAtUtc > bundled.CapturedAtUtc;
    }

    private bool TryReadVerifiedArtifact(string recordId, MediaArtifactReference reference, out byte[] data)
    {
        if (!store.TryReadArtifact(recordId, reference.Key, out data))
        {
            return false;
        }

        return data.LongLength == reference.Length &&
               string.Equals(MediaCacheHashing.Sha256Hex(data), reference.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetArtifactPath(string artifactsDirectory, MediaArtifactReference reference)
    {
        var digest = string.IsNullOrWhiteSpace(reference.Sha256)
            ? MediaCacheHashing.Sha256Hex(reference.Key)
            : reference.Sha256;

        return Path.Combine(artifactsDirectory, digest + ArtifactFileExtension);
    }
}
