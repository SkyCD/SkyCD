using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Metadata.VideoProjects.Models;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.UI.Controls.Preview;
using Xunit;

namespace SkyCD.Plugin.Metadata.VideoProjects.Tests;

/// <summary>
/// End-to-end coverage inside the host pipeline: provider selection, assembly discovery without a manifest,
/// hostile containers (gzip and ZIP bombs, DTD attempts, renamed files, lying RIFX sizes), host resource
/// budgets, cancellation and offline retrieval after the database is reopened.
/// </summary>
public sealed class VideoProjectPluginIntegrationTests : IDisposable
{
    private const string PluginId = "SkyCD.Plugin.VideoProjects";
    private const string MediaRootId = "media-root";
    private const string ProjectEntryId = "file-project-1";

    private readonly string databaseDirectory =
        Path.Combine(Path.GetTempPath(), $"skycd-videoprojects-plugin-{Guid.NewGuid():N}");

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public VideoProjectPluginIntegrationTests()
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
    public void AssemblyDiscovery_FindsMetadataCapability_FromAssemblyMetadata()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(
            typeof(VideoProjectMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("Video Project Metadata", plugin.Name);
        var capability = Assert.IsAssignableFrom<IMediaMetadataCapability>(Assert.Single(plugin.Capabilities));
        Assert.Equal(VideoProjectSchema.MetadataSchemaId, capability.Descriptor.Schema.Id);
        Assert.Equal(VideoProjectSchema.MetadataSchemaVersion, capability.Descriptor.Schema.Version);
        Assert.Equal([".prproj", ".aep", ".aepx", ".drp", ".drt", ".veg"], capability.Descriptor.Extensions);
        Assert.Empty(capability.Descriptor.ContentTypes);
        Assert.Equal(10, capability.Descriptor.Priority);
        Assert.False(capability.Descriptor.IsOptIn);
        Assert.Equal(MediaMetadataType.Number,
            capability.Descriptor.Schema.Fields.Single(field => field.Key == VideoProjectSchema.FrameRate).Type);
        Assert.Equal("fps",
            capability.Descriptor.Schema.Fields.Single(field => field.Key == VideoProjectSchema.FrameRate).Unit);
        Assert.Equal("s",
            capability.Descriptor.Schema.Fields.Single(field => field.Key == VideoProjectSchema.Duration).Unit);
        Assert.Equal(MediaMetadataType.Integer,
            capability.Descriptor.Schema.Fields.Single(field => field.Key == VideoProjectSchema.ClipCount).Type);
    }

    [Fact]
    public void Host_ClaimsOnlyTheDocumentedProjectTypes()
    {
        var manager = CreateManager();

        Assert.Single(manager.ResolveProviders(new MediaItem(".prproj", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".AEP", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".aepx", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".drp", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".DRT", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".veg", null, 10)));

        // No genuine Movie Studio sample proved a .vf layout yet (#1285); other media have own plugins.
        Assert.Empty(manager.ResolveProviders(new MediaItem(".vf", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".mp4", "video/mp4", 10)));
    }

    [Fact]
    public async Task Host_ReadsVegasVersion_FromTheConfirmedHeader()
    {
        var project = VideoProjectFixtures.Vegas(version: 8);
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(".veg", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.False(result.Failed);
        Assert.Equal("Vegas", Text(result, VideoProjectSchema.Kind));
        Assert.Equal("veg", Text(result, VideoProjectSchema.Format));
        Assert.Equal("VEGAS Pro", Text(result, VideoProjectSchema.Application));
        Assert.Equal("8", Text(result, VideoProjectSchema.ApplicationVersion));
        Assert.DoesNotContain(result.Metadata!.Values, value => value.Key == VideoProjectSchema.ProjectTitle);
    }

    [Fact]
    public async Task VegasHeader_IsRecognizedByContent_EvenWhenRenamed()
    {
        var project = VideoProjectFixtures.Vegas();
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(".prproj", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.Equal("Vegas", Text(result, VideoProjectSchema.Kind));
        Assert.Equal("8", Text(result, VideoProjectSchema.ApplicationVersion));
    }

    [Theory]
    [InlineData(40)]
    [InlineData(71)]
    public async Task TruncatedVegasHeader_PublishesNoValues_WithoutFailingThePass(int length)
    {
        var project = VideoProjectFixtures.Vegas()[..length];
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(".veg", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.False(result.Failed);
        Assert.Empty(result.Metadata!.Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Host_ReadsPremiereProject_FromPlainOrGzipXml(bool compressed)
    {
        var project = VideoProjectFixtures.Premiere(compressed);
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(".prproj", "application/octet-stream", project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.False(result.Failed);
        Assert.False(result.TimedOut);
        Assert.Equal(VideoProjectSchema.MetadataSchemaId, result.Schema!.Id);
        Assert.Equal("Premiere", Text(result, VideoProjectSchema.Kind));
        Assert.Equal("prproj", Text(result, VideoProjectSchema.Format));
        Assert.Equal("Adobe Premiere Pro", Text(result, VideoProjectSchema.Application));
        Assert.Equal("25.0", Text(result, VideoProjectSchema.ApplicationVersion));
        Assert.Equal("Main Sequence", Text(result, VideoProjectSchema.ProjectTitle));
        Assert.Equal(2, Integer(result, VideoProjectSchema.TimelineCount));
        Assert.Equal("Main Sequence; B-Roll", Text(result, VideoProjectSchema.TimelineNames));
        Assert.Equal(5, Integer(result, VideoProjectSchema.TrackCount));
        Assert.Equal(3, Integer(result, VideoProjectSchema.ClipCount));
        Assert.Equal(2, Integer(result, VideoProjectSchema.ItemCount));
        Assert.Equal(2, Integer(result, VideoProjectSchema.MediaFileCount));
        Assert.Equal(24.0, Number(result, VideoProjectSchema.FrameRate));
        Assert.Equal(10.0, Number(result, VideoProjectSchema.Duration));
        Assert.Equal("1920x1080", Text(result, VideoProjectSchema.Resolution));
        Assert.DoesNotContain(result.Metadata!.Values, value => value.Key == VideoProjectSchema.IsTemplate);
    }

    [Fact]
    public async Task Host_ReadsBinaryAfterEffectsProject()
    {
        var project = VideoProjectFixtures.Aep();
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(".aep", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.Equal("AfterEffects", Text(result, VideoProjectSchema.Kind));
        Assert.Equal("aep", Text(result, VideoProjectSchema.Format));
        Assert.Equal("Adobe After Effects", Text(result, VideoProjectSchema.Application));
        Assert.Equal("26.2.1", Text(result, VideoProjectSchema.ApplicationVersion));
        Assert.Equal("Main Comp", Text(result, VideoProjectSchema.ProjectTitle));
        Assert.Equal(1, Integer(result, VideoProjectSchema.TimelineCount));
        Assert.Equal(3, Integer(result, VideoProjectSchema.ItemCount));
        Assert.Equal(1, Integer(result, VideoProjectSchema.MediaFileCount));
        Assert.Equal(24.0, Number(result, VideoProjectSchema.FrameRate));
        Assert.Equal(5.0, Number(result, VideoProjectSchema.Duration));
        Assert.Equal("1920x1080", Text(result, VideoProjectSchema.Resolution));
        Assert.DoesNotContain(result.Metadata!.Values, value => value.Key == VideoProjectSchema.Truncated);
    }

    [Fact]
    public async Task Host_ReadsXmlAfterEffectsProject()
    {
        var project = VideoProjectFixtures.Aepx();
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(".aepx", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.Equal("AfterEffects", Text(result, VideoProjectSchema.Kind));
        Assert.Equal("aepx", Text(result, VideoProjectSchema.Format));
        Assert.Equal("26.2.1", Text(result, VideoProjectSchema.ApplicationVersion));
        Assert.Equal("Main Comp", Text(result, VideoProjectSchema.ProjectTitle));
        Assert.Equal(1, Integer(result, VideoProjectSchema.TimelineCount));
        Assert.Equal(3, Integer(result, VideoProjectSchema.ItemCount));
        Assert.Equal(1, Integer(result, VideoProjectSchema.MediaFileCount));
        Assert.Equal(24.0, Number(result, VideoProjectSchema.FrameRate));
        Assert.Equal(5.0, Number(result, VideoProjectSchema.Duration));
        Assert.Equal("1920x1080", Text(result, VideoProjectSchema.Resolution));
    }

    [Theory]
    [InlineData(".drp", "drp")]
    [InlineData(".drt", "drt")]
    public async Task Host_ReadsResolveProject_AndFlagsTemplates(string extension, string format)
    {
        var project = VideoProjectFixtures.Resolve();
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(extension, null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.Equal("Resolve", Text(result, VideoProjectSchema.Kind));
        Assert.Equal(format, Text(result, VideoProjectSchema.Format));
        Assert.Equal("DaVinci Resolve", Text(result, VideoProjectSchema.Application));
        Assert.Equal("19.1.3.0006", Text(result, VideoProjectSchema.ApplicationVersion));
        Assert.Equal("Timeline A", Text(result, VideoProjectSchema.ProjectTitle));
        Assert.Equal(2, Integer(result, VideoProjectSchema.TimelineCount));
        Assert.Equal("Timeline A; Timeline B", Text(result, VideoProjectSchema.TimelineNames));
        Assert.Equal(3, Integer(result, VideoProjectSchema.TrackCount));
        Assert.Equal(3, Integer(result, VideoProjectSchema.ClipCount));
        Assert.Equal(2, Integer(result, VideoProjectSchema.MediaFileCount));
        Assert.Equal(24.0, Number(result, VideoProjectSchema.FrameRate));
        Assert.Equal(10.0, Number(result, VideoProjectSchema.Duration));

        if (extension == ".drt")
        {
            Assert.True(Assert.Single(
                result.Metadata!.Values, value => value.Key == VideoProjectSchema.IsTemplate).Boolean);
        }
        else
        {
            Assert.DoesNotContain(result.Metadata!.Values, value => value.Key == VideoProjectSchema.IsTemplate);
        }
    }

    [Fact]
    public async Task Host_LyingRifxContainerSize_KeepsFactsAndFlagsTruncation()
    {
        var project = VideoProjectFixtures.AepWithLyingContainerSize();
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(".aep", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.Equal("Main Comp", Text(result, VideoProjectSchema.ProjectTitle));
        Assert.True(Assert.Single(
            result.Metadata!.Values, value => value.Key == VideoProjectSchema.Truncated).Boolean);
    }

    [Fact]
    public async Task Host_OverrunningChunk_StopsTheWalkAndFlagsTruncation()
    {
        var project = VideoProjectFixtures.AepWithOverrunChunk();
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(".aep", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.Equal(1, Integer(result, VideoProjectSchema.TimelineCount));
        Assert.True(Assert.Single(
            result.Metadata!.Values, value => value.Key == VideoProjectSchema.Truncated).Boolean);
    }

    [Theory]
    [InlineData(".prproj", "this is not a project file at all")]
    [InlineData(".prproj", "<?xml version=\"1.0\"?><!DOCTYPE PremiereData [<!ENTITY x \"y\">]><PremiereData Version=\"3\"/>")]
    public async Task UnrecognizedOrDtdBearingFile_PublishesNoValues_WithoutFailingThePass(
        string extension, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(extension, null, bytes.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(bytes))));

        Assert.False(result.Failed);
        Assert.Empty(result.Metadata!.Values);
    }

    [Fact]
    public async Task ZipWithoutResolveProject_PublishesNoValues()
    {
        var project = VideoProjectFixtures.ResolveWithoutProject();
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(".drp", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.False(result.Failed);
        Assert.Empty(result.Metadata!.Values);
    }

    [Fact]
    public async Task GzipBomb_IsRefusedByTheDecompressedBound()
    {
        var project = VideoProjectFixtures.GzipBomb(megabytes: 4);
        var manager = CreateManager(new VideoProjectPluginOptions(maxProjectXmlBytes: 1024));

        var result = Assert.Single(await manager.ProcessAsync(
            new MediaItem(".prproj", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.True(result.Failed);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task ArchiveEntryAboveTheBound_IsRefusedBeforeInflation()
    {
        var project = VideoProjectFixtures.Resolve();
        var manager = CreateManager(new VideoProjectPluginOptions(maxArchiveEntryBytes: 512));

        var result = Assert.Single(await manager.ProcessAsync(
            new MediaItem(".drp", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project))));

        Assert.True(result.Failed);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task HostSourceBudget_RejectsProjectsLargerThanTheBudget()
    {
        var project = VideoProjectFixtures.Aep();
        var manager = CreateManager(limits: new MediaExecutionLimits(SourceBytes: 16));

        var results = await manager.ProcessAsync(
            new MediaItem(".aep", null, project.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(project)));

        Assert.All(results, result =>
        {
            Assert.True(result.Failed);
            Assert.Null(result.Metadata);
        });
    }

    [Fact]
    public async Task Scheduler_StoresMetadata_ThatStaysBrowsableAfterReopen()
    {
        var project = VideoProjectFixtures.Premiere(compressed: true);
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, project.Length);
            using var store = MediaCacheStore.Create(database);

            var outcome = await CreateScheduler(store).ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(project)), catalog.GetAll()));

            Assert.True(outcome.Published);
            var record = Assert.Single(store.GetRecordsForEntry(entry.Id));
            Assert.Equal(VideoProjectSchema.MetadataSchemaId, record.SchemaId);
            Assert.Equal(MediaCacheRecordState.Captured, record.State);
        }

        // Reopened with no plugin registered and no source reachable: only the captured fields remain.
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var entry = catalog.GetAll().Single(document => document.Id == ProjectEntryId);
            var snapshot = new MediaPreviewResolver(store).Resolve(entry);

            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal(PreviewPayloadKind.None, snapshot.PayloadKind);
            Assert.Equal("Premiere", FieldText(snapshot, VideoProjectSchema.Kind));
            Assert.Equal("Main Sequence", FieldText(snapshot, VideoProjectSchema.ProjectTitle));
            Assert.Equal(2L, FieldInteger(snapshot, VideoProjectSchema.TimelineCount));
            Assert.Equal(2L, FieldInteger(snapshot, VideoProjectSchema.MediaFileCount));
            Assert.Equal(24.0, FieldNumber(snapshot, VideoProjectSchema.FrameRate));
            Assert.Equal(10.0, FieldNumber(snapshot, VideoProjectSchema.Duration));
            Assert.Equal("1920x1080", FieldText(snapshot, VideoProjectSchema.Resolution));
        }
    }

    [Fact]
    public async Task CancelledExtraction_PublishesNothing()
    {
        var project = VideoProjectFixtures.Resolve();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, project.Length);
        using var store = MediaCacheStore.Create(database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateScheduler(store).ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(project)), catalog.GetAll()),
            progress: null,
            cancelled.Token));

        Assert.Empty(store.GetRecordsForEntry(entry.Id));
    }

    private static string Text(MediaProviderResult result, string key) =>
        Value(result, key).Text
        ?? throw new InvalidOperationException($"Field '{key}' is not text.");

    private static long Integer(MediaProviderResult result, string key) =>
        Value(result, key).Integer
        ?? throw new InvalidOperationException($"Field '{key}' is not an integer.");

    private static double Number(MediaProviderResult result, string key) =>
        Value(result, key).Number
        ?? throw new InvalidOperationException($"Field '{key}' is not a number.");

    private static MediaMetadataValue Value(MediaProviderResult result, string key) =>
        result.Metadata!.Values.Single(value => value.Key == key);

    private static string FieldText(EntryPreviewSnapshot snapshot, string key) =>
        snapshot.Fields.Single(field => field.Key == key).Value as string
        ?? throw new InvalidOperationException($"Field '{key}' is not text.");

    private static long FieldInteger(EntryPreviewSnapshot snapshot, string key) =>
        snapshot.Fields.Single(field => field.Key == key).Value is long value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not an integer.");

    private static double FieldNumber(EntryPreviewSnapshot snapshot, string key) =>
        snapshot.Fields.Single(field => field.Key == key).Value is double value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not a number.");

    private static MediaProvider[] Providers(VideoProjectPluginOptions? options = null) =>
    [
        new MediaProvider(PluginId, "1.0.0",
            new VideoProjectMetadataPlugin(options ?? VideoProjectPluginOptions.Default))
    ];

    private static MediaExtensionManager CreateManager(
        VideoProjectPluginOptions? options = null, MediaExecutionLimits? limits = null) =>
        new(Providers(options), limits);

    private static MediaExtractionScheduler CreateScheduler(IMediaCacheStore store) =>
        new(new MediaExtensionManager(Providers()), store);

    private Database OpenDatabase() =>
        new("videoprojects-plugin-tests", new DatabaseConfiguration { Directory = databaseDirectory });

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
            Name = "Projects",
            ParentId = "library",
            Type = CatalogDocumentType.Media
        };
        var entry = new CatalogDocument
        {
            Id = ProjectEntryId,
            Name = "interview.prproj",
            ParentId = root.Id,
            Type = CatalogDocumentType.File,
            Size = entrySize,
            Properties = new PropertiesCollection
            {
                ["FullName"] = @"D:\Projects\interview.prproj",
                ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
            }
        };

        repository.Save(root.Id, root);
        repository.Save(entry.Id, entry);
        return entry;
    }
}
