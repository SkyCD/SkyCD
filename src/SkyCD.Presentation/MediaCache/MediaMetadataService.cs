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

public sealed class MediaMetadataService
{
    public const string SourcePathPropertyKey = "FullName";

    private const int FileBufferBytes = 128 * 1024;
    private readonly MediaExtensionManager manager;
    private readonly MediaExtractionScheduler scheduler;

    public MediaMetadataService(MediaExtensionManager manager, IMediaCacheStore store, TimeProvider? timeProvider = null)
    {
        this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        scheduler = new MediaExtractionScheduler(manager, store, timeProvider);
    }

    public bool CanCollect => manager.HasMetadataProviders;

    public async Task<MediaMetadataOutcome> CollectAsync(
        CatalogDocument entry,
        IReadOnlyList<CatalogDocument> catalog,
        IProgress<MediaProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? batchId = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(catalog);

        var path = ReadString(entry.Properties, SourcePathPropertyKey);
        if (string.IsNullOrWhiteSpace(path))
        {
            return MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.NoSourcePath);
        }

        FileInfo file;
        try
        {
            file = new FileInfo(path);
            if (!file.Exists)
            {
                return MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.SourceUnavailable);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException or NotSupportedException)
        {
            return MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.SourceUnavailable);
        }

        if (file.Length != entry.Size || !MatchesRecordedTimestamp(entry, file))
        {
            return MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.SourceChanged);
        }

        var stored = false;
        MediaMetadataOutcome? failure = null;
        foreach (var selection in new[] { MediaProviderSelection.StandardMetadata, MediaProviderSelection.OptInMetadata })
        {
            var request = MediaExtractionRequest.ForCatalogEntry(
                entry, token => OpenReadAsync(path, token), catalog, batchId: batchId, providerSelection: selection);
            if (manager.ResolveProviders(request.Item, selection).Count == 0)
            {
                continue;
            }

            var limits = selection == MediaProviderSelection.OptInMetadata
                ? new MediaExecutionLimits(SourceBytes: Math.Max(file.Length, 1), Concurrency: 1,
                    TimeoutMilliseconds: int.MaxValue)
                : null;
            var outcome = await scheduler.ProcessAsync(request, progress, cancellationToken, limits).ConfigureAwait(false);
            if (outcome.SourceUnavailable)
            {
                failure ??= MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.SourceUnavailable);
                continue;
            }

            var failed = outcome.Records.FirstOrDefault(record => record.State == MediaCacheRecordState.Failed);
            if (failed is not null)
            {
                failure ??= new MediaMetadataOutcome(entry.Id, Stored: false, MediaMetadataSkipReason.Failed, failed.FailureReason);
                continue;
            }

            stored = true;
        }

        return failure ?? (stored
            ? new MediaMetadataOutcome(entry.Id, Stored: true, MediaMetadataSkipReason.None, FailureReason: null)
            : MediaMetadataOutcome.Skipped(entry.Id, MediaMetadataSkipReason.NoProvider));
    }

    private static Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            FileBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
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
        properties.TryGetValue(key, out var value) && value is not null ? value.ToString() : null;
}

public enum MediaMetadataSkipReason
{
    None,
    NoSourcePath,
    NoProvider,
    SourceUnavailable,
    SourceChanged,
    Failed
}

public sealed record MediaMetadataOutcome(string EntryId, bool Stored, MediaMetadataSkipReason SkipReason, string? FailureReason)
{
    public static MediaMetadataOutcome Skipped(string entryId, MediaMetadataSkipReason reason) =>
        new(entryId, Stored: false, reason, FailureReason: null);
}
