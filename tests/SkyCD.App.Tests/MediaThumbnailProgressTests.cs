using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.MediaCache;
using SkyCD.SDK.Capabilities;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #944: extraction reports each freshly produced preview artifact once, so the adding progress
/// window can show the newest thumbnail while the add is still running.
/// </summary>
public sealed class MediaThumbnailProgressTests : IDisposable
{
    private const string EntryId = "file-thumb-1";
    private const string MediaRootId = "media-thumb-root";

    private static readonly byte[] ThumbnailBytes = [0x89, 0x50, 0x4E, 0x47];

    private readonly string sourceDirectory =
        Path.Combine(Path.GetTempPath(), $"skycd-thumbnail-{Guid.NewGuid():N}");

    private readonly string sourcePath;
    private readonly CatalogDocument root;
    private readonly CatalogDocument entry;

    public MediaThumbnailProgressTests()
    {
        Directory.CreateDirectory(sourceDirectory);
        sourcePath = Path.Combine(sourceDirectory, "poster.mp4");
        File.WriteAllBytes(sourcePath, new byte[64]);

        root = new CatalogDocument
        {
            Id = MediaRootId,
            Name = "Media",
            ParentId = "library",
            Type = CatalogDocumentType.Media
        };
        entry = new CatalogDocument
        {
            Id = EntryId,
            Name = "poster.mp4",
            ParentId = root.Id,
            Type = CatalogDocumentType.File,
            Size = 64,
            Properties = new PropertiesCollection
            {
                ["FullName"] = sourcePath,
                ["LastWriteTime"] = File.GetLastWriteTimeUtc(sourcePath).ToString("O")
            }
        };
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(sourceDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task ProcessAsync_ReportsTheCapturedPreviewArtifactOnce()
    {
        using var store = new InMemoryMediaCacheStore();
        var scheduler = new MediaExtractionScheduler(
            new MediaExtensionManager([new MediaProvider("plugin.preview", "1.0.0", new PreviewCapability())]),
            store);
        var reported = new List<MediaThumbnail>();

        var outcome = await scheduler.ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(entry, OpenSource, Catalog),
            thumbnailProgress: new SyncProgress<MediaThumbnail>(reported.Add));

        Assert.True(outcome.Published);
        var thumbnail = Assert.Single(reported);
        Assert.Equal("poster.mp4", thumbnail.Name);
        Assert.Equal("image/png", thumbnail.ContentType);
        Assert.Equal(ThumbnailBytes, thumbnail.Data);

        var record = Assert.Single(store.GetRecordsForEntry(EntryId));
        Assert.True(store.TryReadArtifact(record.Id, "preview:0", out var stored));
        Assert.Equal(ThumbnailBytes, stored);
    }

    [Fact]
    public async Task CollectAsync_ReportsAtMostOneThumbnailPerEntry()
    {
        using var store = new InMemoryMediaCacheStore();
        var service = new MediaMetadataService(new MediaExtensionManager([
            new MediaProvider("plugin.preview", "1.0.0", new PreviewCapability()),
            new MediaProvider("plugin.tags", "1.0.0", new PreviewCapability { SchemaId = "fake.tags" }),
            new MediaProvider("plugin.audio", "1.0.0", new MetadataOnlyCapability())
        ]), store);
        Assert.True(service.CanCollect);

        var reported = new List<MediaThumbnail>();
        var firstReport = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var outcome = await service.CollectAsync(entry, Catalog,
            thumbnailProgress: new SyncProgress<MediaThumbnail>(thumbnail =>
            {
                reported.Add(thumbnail);
                firstReport.TrySetResult();
            }));

        Assert.True(outcome.Stored);
        await firstReport.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var thumbnail = Assert.Single(reported);
        Assert.Equal("poster.mp4", thumbnail.Name);
        Assert.Equal(ThumbnailBytes, thumbnail.Data);
    }

    [Fact]
    public async Task CollectAsync_ReportsNothingWhenNoPreviewIsProduced()
    {
        using var store = new InMemoryMediaCacheStore();
        var service = new MediaMetadataService(
            new MediaExtensionManager([new MediaProvider("plugin.audio", "1.0.0", new MetadataOnlyCapability())]),
            store);

        var reported = new List<MediaThumbnail>();

        await service.CollectAsync(entry, Catalog,
            thumbnailProgress: new SyncProgress<MediaThumbnail>(reported.Add));

        Assert.Empty(reported);
    }

    private IReadOnlyList<CatalogDocument> Catalog => [root, entry];

    private static Task<Stream> OpenSource(CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(new MemoryStream(new byte[64]));

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    private sealed class PreviewCapability : IMediaPreviewCapability
    {
        public string SchemaId { get; init; } = "fake.preview";

        public MediaDescriptor Descriptor => new(
            [".mp4"],
            [],
            new MediaMetadataSchema(SchemaId, 1, []),
            IndexTimePreview: true);

        public Task<MediaPreviewResult> GeneratePreviewAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaPreviewResult([new MediaPreviewArtifact("image/png", ThumbnailBytes)]));
    }

    private sealed class MetadataOnlyCapability : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor => new(
            [".mp4"],
            [],
            new MediaMetadataSchema("fake.audio", 1, [new("Title", MediaMetadataType.Text)]));

        public Task<MediaExtractionResult> ExtractAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken) =>
            Task.FromResult(new MediaExtractionResult([new("Title", MediaMetadataType.Text, Text: "Poster")]));
    }
}
