using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.TestSupport;
using SkyCD.UI.Controls.Preview;
using Xunit;
using SkyCD.Plugin.Metadata.Image.Models;

namespace SkyCD.Plugin.Metadata.Image.Tests;

/// <summary>
/// End-to-end coverage for the image plugin inside the host pipeline: provider selection, host resource
/// budgets, cancellation, failure isolation, assembly discovery without a manifest, and the property that
/// matters most for the catalog browser — captured previews and metadata stay readable after the database is
/// reopened with the media disconnected and the plugin unloaded.
/// </summary>
public sealed class ImagePluginIntegrationTests : IDisposable
{
    private const string PluginId = "SkyCD.Plugin.Image";
    private const string MediaRootId = "media-root";
    private const string PhotoEntryId = "file-photo-1";

    private readonly TestWorkspace workspace = new();

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    private string databaseDirectory => workspace.Root;

    public ImagePluginIntegrationTests()
    {
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", databaseDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);
        workspace.Dispose();
    }

    [Fact]
    public async Task Host_RunsBothCapabilitiesAndReturnsTypedMetadataWithAPreview()
    {
        var image = ImageFixtures.JpegWithExif(60, 40, orientation: 6, new DateTime(2024, 3, 1, 12, 0, 0));
        var results = await RunHostAsync(image, ".jpg", "image/jpeg");

        var metadata = results.Single(result => result.Metadata is not null);
        Assert.False(metadata.Failed);
        Assert.Equal("skycd.image.metadata", metadata.Schema!.Id);
        Assert.Equal(40L, metadata.Metadata!.Values.Single(value => value.Key == ImageSchema.Width).Integer);
        Assert.Equal(60L, metadata.Metadata.Values.Single(value => value.Key == ImageSchema.Height).Integer);

        var preview = results.Single(result => result.Preview is not null);
        Assert.False(preview.Failed);
        var artifact = Assert.Single(preview.Preview!.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        Assert.Equal((40, 60), ImageFixtures.Dimensions(artifact.Data));
    }

    [Fact]
    public void Host_ClaimsOnlyTheDocumentedImageTypes()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".jpg", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".PNG", "image/png", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".mp4", "video/mp4", 10)));
    }

    [Fact]
    public async Task HostSourceBudget_RejectsAnImageLargerThanTheBudget()
    {
        var image = ImageFixtures.Jpeg(400, 400);
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 64));

        var results = await manager.ProcessAsync(
            new MediaItem(".jpg", "image/jpeg", image.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(image)));

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public async Task MalformedImage_IsReportedAsAFailedProviderInsteadOfBreakingThePass()
    {
        var results = await RunHostAsync(ImageFixtures.NotAnImage(), ".jpg", "image/jpeg");

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.Failed));
        Assert.All(results, result => Assert.Null(result.Metadata));
        Assert.All(results, result => Assert.Null(result.Preview));
    }

    [Fact]
    public async Task CancelledExtraction_PublishesNothing()
    {
        var image = ImageFixtures.Jpeg(48, 32);
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, image.Length);
        using var store = MediaCacheStore.Create(database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var scheduler = CreateScheduler(store);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scheduler.ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(entry, _ => Task.FromResult<Stream>(new MemoryStream(image)), catalog.GetAll()),
            progress: null,
            cancelled.Token));

        Assert.Empty(store.GetRecordsForEntry(entry.Id));
    }

    [Fact]
    public async Task CapturedImageResults_SurviveReopen_WithoutThePluginOrTheSource()
    {
        var captured = new DateTime(2024, 3, 1, 12, 0, 0);
        var image = ImageFixtures.JpegWithExif(60, 40, orientation: 6, captured);

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, image.Length);
            using var store = MediaCacheStore.Create(database);

            var outcome = await CreateScheduler(store).ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(image)), catalog.GetAll()));

            Assert.True(outcome.Published);
        }

        // Reopened with no plugin registered and no source reachable: only stored bytes remain.
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var entry = catalog.GetAll().Single(document => document.Id == PhotoEntryId);

            var resolver = new MediaPreviewResolver(store);
            var snapshot = resolver.Resolve(entry);

            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
            Assert.Equal(PluginId, snapshot.ProviderId);
            Assert.True(resolver.TryReadPayload(snapshot, out var payload));

            // The stored thumbnail is a real, upright, decodable image.
            Assert.Equal((40, 60), ImageFixtures.Dimensions(payload));

            Assert.Equal(40L, Field(snapshot, ImageSchema.Width));
            Assert.Equal(60L, Field(snapshot, ImageSchema.Height));
            Assert.Equal("jpeg", snapshot.Fields.Single(field => field.Key == ImageSchema.Format).Value);
            Assert.Equal(6L, Field(snapshot, ImageSchema.Orientation));
            Assert.Equal(90L, Field(snapshot, ImageSchema.RotationDegrees));
            Assert.Equal(
                new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero),
                snapshot.Fields.Single(field => field.Key == ImageSchema.CapturedAt).Value);
        }
    }

    [Fact]
    public async Task IndexedContainerPreviewSurvivesReopenWithoutPluginOrSource()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 20">
                <title>Offline drawing</title><rect width="20" height="20" fill="blue"/>
            </svg>
            """u8.ToArray();

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, svg.Length, "drawing.svg");
            using var store = MediaCacheStore.Create(database);
            var manager = new MediaExtensionManager(
            [
                new MediaProvider(PluginId, "1.0.0", new VectorImagePlugin()),
                new MediaProvider(PluginId, "1.0.0", new VectorImagePreviewPlugin())
            ]);
            var scheduler = new MediaExtractionScheduler(manager, store);
            var outcome = await scheduler.ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(svg)), catalog.GetAll()));

            Assert.True(outcome.Published);
        }

        using (var database = OpenDatabase())
        {
            var entry = CreateCatalogRepository(database).GetAll().Single(document => document.Id == PhotoEntryId);
            using var store = MediaCacheStore.Create(database);
            var resolver = new MediaPreviewResolver(store);
            var snapshot = resolver.Resolve(entry);
            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal("Offline drawing", snapshot.Fields.Single(field => field.Key == "Title").Value);
            Assert.True(resolver.TryReadPayload(snapshot, out var data));
            Assert.Equal((40, 20), ImageFixtures.Dimensions(data));
        }
    }

    [Fact]
    public async Task TransparentPng_RoundTripsThroughTheCacheAsPng()
    {
        var image = ImageFixtures.TransparentPng(40, 20);

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, image.Length);
            using var store = MediaCacheStore.Create(database);

            var outcome = await CreateScheduler(store).ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(image)), catalog.GetAll()));

            Assert.True(outcome.Published);
        }

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var entry = catalog.GetAll().Single(document => document.Id == PhotoEntryId);

            var resolver = new MediaPreviewResolver(store);
            var snapshot = resolver.Resolve(entry);

            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.True(resolver.TryReadPayload(snapshot, out var payload));
            Assert.Equal("png", snapshot.Fields.Single(field => field.Key == ImageSchema.Format).Value);
            Assert.True(snapshot.Fields.Single(field => field.Key == ImageSchema.HasAlpha).Value is true);
            Assert.Equal("image/png", snapshot.Artifact!.ContentType);
            Assert.Equal(0, SkiaSharp.SKBitmap.Decode(payload)!.GetPixel(2, 10).Alpha);
        }
    }

    [Fact]
    public async Task AnimatedGif_IsStoredAsASingleFramePreviewWithItsFrameCountInMetadata()
    {
        var image = ImageFixtures.AnimatedGif(8, 6, frameCount: 3);

        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, image.Length);
        using var store = MediaCacheStore.Create(database);

        var outcome = await CreateScheduler(store).ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(image)), catalog.GetAll()));

        Assert.True(outcome.Published);

        var resolver = new MediaPreviewResolver(store);
        var snapshot = resolver.Resolve(entry);

        Assert.Equal(MediaPreviewState.Ready, snapshot.State);
        Assert.Equal(3L, Field(snapshot, ImageSchema.FrameCount));
        Assert.Equal(true, snapshot.Fields.Single(field => field.Key == ImageSchema.IsAnimated).Value);
        Assert.True(resolver.TryReadPayload(snapshot, out var payload));
        Assert.Equal((8, 6), ImageFixtures.Dimensions(payload));
    }

    [Fact]
    public void AssemblyDiscovery_FindsEveryCapability_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(ImageMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.NotNull(plugin.Version);
        Assert.Equal("Image Preview and Metadata", plugin.Name);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaPreviewCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is ContainerTagsPlugin);
        Assert.Equal(11, plugin.Capabilities.Count);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static long Field(EntryPreviewSnapshot snapshot, string key) =>
        snapshot.Fields.Single(field => field.Key == key).Value is long value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not an integer.");

    private async Task<IReadOnlyList<MediaProviderResult>> RunHostAsync(byte[] image, string extension, string contentType) =>
        await CreateManager().ProcessAsync(
            new MediaItem(extension, contentType, image.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(image)));

    private static MediaExtensionManager CreateManager(MediaExecutionLimits? limits = null) =>
        new(new[]
        {
            new MediaProvider(PluginId, "1.0.0", new ImageMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new ImagePreviewPlugin())
        }, limits);

    private static MediaExtractionScheduler CreateScheduler(IMediaCacheStore store) =>
        new(new MediaExtensionManager(new[]
        {
            new MediaProvider(PluginId, "1.0.0", new ImageMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new ImagePreviewPlugin())
        }), store);

    private Database OpenDatabase() =>
        new("image-plugin-tests", new DatabaseConfiguration { Directory = databaseDirectory });

    private static CatalogDocumentRepository CreateCatalogRepository(Database database)
    {
        var collection = database.GetCollection("catalog", Collection.DefaultScopeName)
                         ?? database.CreateCollection("catalog", Collection.DefaultScopeName);

        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), "catalog", collection);
        return repository;
    }

    private static CatalogDocument SeedCatalog(CatalogDocumentRepository repository, long entrySize, string name = "holiday.jpg")
    {
        var root = new CatalogDocument
        {
            Id = MediaRootId,
            Name = "Photos",
            ParentId = "library",
            Type = CatalogDocumentType.Media
        };

        var entry = new CatalogDocument
        {
            Id = PhotoEntryId,
            Name = name,
            ParentId = root.Id,
            Type = CatalogDocumentType.File,

            // The host builds MediaItem.Length from the catalogued size and reads exactly that many bytes,
            // so this has to match the fixture or the source runs past the end of the stream.
            Size = entrySize,
            Properties = new SkyCD.Documents.Collections.PropertiesCollection
            {
                ["FullName"] = $@"D:\Photos\{name}",
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
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.Image");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the image plugin project directory.");
    }
}
