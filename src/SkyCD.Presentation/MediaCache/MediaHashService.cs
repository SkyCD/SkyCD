using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Explicitly computes whole-file hashes for catalogued files. Hashing is opt-in by design: it opens the
/// original only when the user asks for it (or enabled hashing during scans) and never as a side effect of
/// selecting an entry.
/// </summary>
public sealed class MediaHashService
{
    /// <summary>Catalog property holding the absolute path recorded by the scanner.</summary>
    public const string SourcePathPropertyKey = "FullName";

    private const int FileBufferBytes = 128 * 1024;

    private readonly MediaExtractionScheduler scheduler;
    private readonly TimeProvider timeProvider;

    public MediaHashService(MediaExtensionManager manager, IMediaCacheStore store, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(store);

        this.timeProvider = timeProvider ?? TimeProvider.System;
        scheduler = new MediaExtractionScheduler(manager, store, this.timeProvider);
    }

    /// <summary>
    /// Hashes one file and stores the result. A missing, changed or unreadable source leaves previously
    /// captured hashes untouched and reports why nothing was stored.
    /// </summary>
    public async Task<MediaHashOutcome> ComputeAsync(
        CatalogDocument entry,
        IReadOnlyList<CatalogDocument> catalog,
        IProgress<MediaProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(catalog);

        var path = ReadString(entry.Properties, SourcePathPropertyKey);
        if (string.IsNullOrWhiteSpace(path))
        {
            return MediaHashOutcome.Skipped(entry.Id, MediaHashSkipReason.NoSourcePath);
        }

        FileInfo file;
        try
        {
            file = new FileInfo(path);
            if (!file.Exists)
            {
                return MediaHashOutcome.Skipped(entry.Id, MediaHashSkipReason.SourceUnavailable);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
        {
            return MediaHashOutcome.Skipped(entry.Id, MediaHashSkipReason.SourceUnavailable);
        }

        // The fingerprint was built from the catalog facts. Hashing different bytes and storing them under
        // that fingerprint would publish a digest that no longer identifies the catalogued source.
        if (file.Length != entry.Size || !MatchesRecordedTimestamp(entry, file))
        {
            return MediaHashOutcome.Skipped(entry.Id, MediaHashSkipReason.SourceChanged);
        }

        var request = MediaExtractionRequest.ForCatalogEntry(
            entry,
            token => OpenReadAsync(path, token),
            catalog,
            forceRefresh: true,
            providerSelection: MediaProviderSelection.OptInOnly);

        var limits = new MediaExecutionLimits(
            SourceBytes: Math.Max(file.Length, 1),
            Concurrency: 1,
            TimeoutMilliseconds: int.MaxValue);

        var outcome = await scheduler.ProcessAsync(request, progress, cancellationToken, limits)
            .ConfigureAwait(false);

        if (outcome.SourceUnavailable)
        {
            return MediaHashOutcome.Skipped(entry.Id, MediaHashSkipReason.SourceUnavailable);
        }

        var failed = outcome.Records.FirstOrDefault(record => record.State == MediaCacheRecordState.Failed);
        return failed is null
            ? new MediaHashOutcome(entry.Id, Stored: true, MediaHashSkipReason.None, FailureReason: null)
            : new MediaHashOutcome(entry.Id, Stored: false, MediaHashSkipReason.Failed, failed.FailureReason);
    }

    private static Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            FileBufferBytes,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    private static bool MatchesRecordedTimestamp(CatalogDocument entry, FileInfo file)
    {
        if (!entry.Properties.TryGetValue("LastWriteTime", out var raw) || raw is null)
        {
            return true;
        }

        var text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text) ||
            !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var recorded))
        {
            return true;
        }

        return file.LastWriteTimeUtc == recorded.UtcDateTime;
    }

    private static string? ReadString(SkyCD.Documents.Collections.PropertiesCollection properties, string key) =>
        properties.TryGetValue(key, out var value) && value is not null
            ? value.ToString()
            : null;
}

/// <summary>Why a compute request did or did not store hashes.</summary>
public enum MediaHashSkipReason
{
    None,
    NoSourcePath,
    SourceUnavailable,
    SourceChanged,
    Failed
}

/// <summary>Result of one explicit hash request.</summary>
public sealed record MediaHashOutcome(string EntryId, bool Stored, MediaHashSkipReason SkipReason, string? FailureReason)
{
    public static MediaHashOutcome Skipped(string entryId, MediaHashSkipReason reason) =>
        new(entryId, Stored: false, reason, FailureReason: null);
}
