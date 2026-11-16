using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Controls.Preview;
using Xunit;

namespace SkyCD.Plugin.Video.Tests;

/// <summary>
/// End-to-end coverage for the video plugin inside the host pipeline: provider selection, host resource
/// budgets, the host's own timeout around a slow decoder, cancellation, failure isolation, assembly discovery
/// without a manifest, and offline retrieval after the database is reopened with the media gone and the
/// plugin unloaded.
/// </summary>
public sealed class VideoPluginIntegrationTests : IDisposable
{
    private const string PluginId = "SkyCD.Plugin.Video";
    private const string MediaRootId = "media-root";
    private const string VideoEntryId = "file-video-1";

    private readonly string databaseDirectory =
        Path.Combine(Path.GetTempPath(), $"skycd-video-plugin-{Guid.NewGuid():N}");

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public VideoPluginIntegrationTests()
    {
        Directory.CreateDirectory(databaseDirectory);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", databaseDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);

        try
        {
            if (Directory.Exists(databaseDirectory))
            {
                Directory.Delete(databaseDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Couchbase Lite can release file handles slightly after dispose on some systems.
        }
        catch (UnauthorizedAccessException)
        {
            // Blob files can stay locked briefly after the database is closed on Windows.
        }
    }

    [Fact]
    public async Task Host_RunsBothCapabilitiesForEverySupportedContainer()
    {
        foreach (var (video, extension, contentType) in SupportedSources())
        {
            var results = await CreateManager().ProcessAsync(
                new MediaItem(extension, contentType, video.Length),
                _ => Task.FromResult<Stream>(new MemoryStream(video)));

            var metadata = results.Single(result => result.Metadata is not null);
            var preview = results.Single(result => result.Preview is not null);

            Assert.False(metadata.Failed);
            Assert.False(preview.Failed);
            Assert.Equal("skycd.video.metadata", metadata.Schema!.Id);
            Assert.Equal("skycd.video.preview", preview.Schema!.Id);
            Assert.NotEmpty(preview.Preview!.Artifacts);
        }
    }

    [Fact]
    public void Host_ClaimsOnlyTheDocumentedVideoTypes()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".mp4", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".MKV", "video/x-matroska", 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".webm", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".avi", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".mov", "video/quicktime", 10)));

        // Audio and still images belong to their own plugins.
        Assert.Empty(manager.ResolveProviders(new MediaItem(".m4a", "audio/mp4", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".jpg", "image/jpeg", 10)));
    }

    [Fact]
    public async Task MalformedVideo_IsReportedAsAFailedProviderInsteadOfBreakingThePass()
    {
        var video = VideoFixtures.NotVideo();
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".mp4", "video/mp4", video.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(video)));

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.Failed));
        Assert.All(results, result => Assert.Null(result.Metadata));
    }

    [Fact]
    public async Task HostSourceBudget_RejectsVideoLargerThanTheBudget()
    {
        var video = VideoFixtures.Mp4();
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 16));

        var results = await manager.ProcessAsync(
            new MediaItem(".mp4", "video/mp4", video.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(video)));

        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public async Task SlowDecoder_IsTimedOutByTheHostWithoutHangingThePass()
    {
        var video = VideoFixtures.Mp4();
        var manager = CreateManager(new MediaExecutionLimits(TimeoutMilliseconds: 150), slowDecoderDelay: 10_000);

        var results = await manager.ProcessAsync(
            new MediaItem(".mp4", "video/mp4", video.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(video)));

        var preview = results.Single(result => result.Capability.Contains(nameof(VideoPosterPlugin), StringComparison.Ordinal));
        Assert.True(preview.Failed);
        Assert.True(preview.TimedOut);

        // Metadata does not depend on the decoder, so it still succeeds in the same pass.
        var metadata = results.Single(result => result.Metadata is not null);
        Assert.False(metadata.Failed);
    }

    [Fact]
    public async Task CapturedMetadataAndPoster_SurviveReopen_WithoutThePluginOrTheSource()
    {
        var video = VideoFixtures.Mp4(rotationDegrees: 90);

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, video.Length);
            using var store = MediaCacheStore.Create(database);

            var outcome = await CreateScheduler(store).ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(video)), catalog.GetAll()));

            Assert.True(outcome.Published);
        }

        // Reopened with no plugin registered and no source reachable: only stored bytes remain.
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var entry = catalog.GetAll().Single(document => document.Id == VideoEntryId);

            var resolver = new MediaPreviewResolver(store);
            var snapshot = resolver.Resolve(entry);

            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
            Assert.Equal(PluginId, snapshot.ProviderId);
            Assert.True(resolver.TryReadPayload(snapshot, out var payload));
            Assert.Equal(VideoFixtures.JpegFrame, payload);

            Assert.Equal("mp4", FieldText(snapshot, VideoSchema.Container));
            Assert.Equal(5.0, FieldNumber(snapshot, VideoSchema.Duration));
            Assert.Equal(1920L, FieldInteger(snapshot, VideoSchema.Width));
            Assert.Equal(1080L, FieldInteger(snapshot, VideoSchema.Height));
            Assert.Equal(90L, FieldInteger(snapshot, VideoSchema.RotationDegrees));
            Assert.Equal("h264", FieldText(snapshot, VideoSchema.VideoCodec));
            Assert.Equal("aac", FieldText(snapshot, VideoSchema.AudioCodec));
            Assert.Equal(24.0, FieldNumber(snapshot, VideoSchema.FrameRate));
            Assert.Equal("Test Video", FieldText(snapshot, VideoSchema.Title));
        }
    }

    [Fact]
    public async Task MetadataOnlyResult_IsStoredWhenNoDecoderIsAvailable()
    {
        var video = VideoFixtures.Matroska();

        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, video.Length);
        using var store = MediaCacheStore.Create(database);

        var outcome = await CreateScheduler(store, decoderAvailable: false).ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(video)), catalog.GetAll()));

        Assert.True(outcome.Published);

        var snapshot = new MediaPreviewResolver(store).Resolve(entry);

        // No artifact was produced, yet the captured fields are still browsable offline.
        Assert.Equal(MediaPreviewState.Ready, snapshot.State);
        Assert.Equal(PreviewPayloadKind.None, snapshot.PayloadKind);
        Assert.Equal("mkv", FieldText(snapshot, VideoSchema.Container));
        Assert.Equal(1280L, FieldInteger(snapshot, VideoSchema.Width));
    }

    [Fact]
    public async Task CancelledExtraction_PublishesNothing()
    {
        var video = VideoFixtures.Mp4();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, video.Length);
        using var store = MediaCacheStore.Create(database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateScheduler(store).ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(video)), catalog.GetAll()),
            progress: null,
            cancelled.Token));

        Assert.Empty(store.GetRecordsForEntry(entry.Id));
    }

    [Fact]
    public void AssemblyDiscovery_FindsBothCapabilities_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(VideoMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("Video Metadata and Poster Frames", plugin.Name);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaPreviewCapability);
        Assert.Equal(2, plugin.Capabilities.Count);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static IEnumerable<(byte[] Video, string Extension, string ContentType)> SupportedSources()
    {
        yield return (VideoFixtures.Mp4(), ".mp4", "video/mp4");
        yield return (VideoFixtures.Matroska(), ".mkv", "video/x-matroska");
        yield return (VideoFixtures.Webm(), ".webm", "video/webm");
        yield return (VideoFixtures.Avi(), ".avi", "video/x-msvideo");
    }

    private static string FieldText(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value as string
        ?? throw new InvalidOperationException($"Field '{key}' is not text.");

    private static long FieldInteger(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value is long value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not an integer.");

    private static double FieldNumber(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value is double value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not a number.");

    private static MediaMetadataFieldView Field(EntryPreviewSnapshot snapshot, string key) =>
        snapshot.Fields.Single(field => field.Key == key);

    private static MediaProvider[] Providers(bool decoderAvailable, int slowDecoderDelayMilliseconds) =>
    [
        new MediaProvider(PluginId, "1.0.0", new VideoMetadataPlugin()),
        new MediaProvider(PluginId, "1.0.0", new VideoPosterPlugin(
            VideoPluginOptions.Default,
            new StubExtractor(decoderAvailable, VideoFixtures.JpegFrame, slowDecoderDelayMilliseconds)))
    ];

    private static MediaExtensionManager CreateManager(
        MediaExecutionLimits? limits = null,
        bool decoderAvailable = true,
        int slowDecoderDelay = 0) =>
        new(Providers(decoderAvailable, slowDecoderDelay), limits);

    private static MediaExtractionScheduler CreateScheduler(IMediaCacheStore store, bool decoderAvailable = true) =>
        new(new MediaExtensionManager(Providers(decoderAvailable, 0)), store);

    private Database OpenDatabase() =>
        new("video-plugin-tests", new DatabaseConfiguration { Directory = databaseDirectory });

    private static CatalogDocumentRepository CreateCatalogRepository(Database database)
    {
        var collection = database.GetCollection("catalog", Collection.DefaultScopeName)
                         ?? database.CreateCollection("catalog", Collection.DefaultScopeName);

        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), "catalog", collection);
        return repository;
    }

    private static CatalogDocument SeedCatalog(CatalogDocumentRepository repository, long entrySize)
    {
        var root = new CatalogDocument
        {
            Id = MediaRootId,
            Name = "Videos",
            ParentId = "library",
            Type = CatalogDocumentType.Media
        };

        var entry = new CatalogDocument
        {
            Id = VideoEntryId,
            Name = "holiday.mp4",
            ParentId = root.Id,
            Type = CatalogDocumentType.File,

            // The host builds MediaItem.Length from the catalogued size and reads exactly that many bytes.
            Size = entrySize,
            Properties = new PropertiesCollection
            {
                ["FullName"] = @"D:\Videos\holiday.mp4",
                ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
            }
        };

        repository.Save(root.Id, root);
        repository.Save(entry.Id, entry);
        return entry;
    }

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Video");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the video plugin project directory.");
    }

    /// <summary>Stands in for an external decoder, optionally slowly, so host timeouts can be exercised.</summary>
    private sealed class StubExtractor : IVideoFrameExtractor
    {
        private readonly bool available;
        private readonly byte[]? frame;
        private readonly int delayMilliseconds;

        public StubExtractor(bool available, byte[]? frame, int delayMilliseconds)
        {
            this.available = available;
            this.frame = frame;
            this.delayMilliseconds = delayMilliseconds;
        }

        public bool IsAvailable => available;

        public string Describe() => available ? "stub decoder" : "no decoder";

        public async Task<byte[]?> ExtractJpegAsync(
            Func<Stream, CancellationToken, Task> writeSourceAsync,
            double timestampSeconds,
            int maxDimension,
            CancellationToken cancellationToken)
        {
            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds, cancellationToken);
            }

            using var buffer = new MemoryStream();
            await writeSourceAsync(buffer, cancellationToken);
            return frame;
        }
    }
}
