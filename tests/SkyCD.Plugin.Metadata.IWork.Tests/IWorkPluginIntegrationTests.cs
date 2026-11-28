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
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.TestSupport;
using SkyCD.UI.Controls.Preview;
using Xunit;
using SkyCD.Plugin.Metadata.IWork.Models;

namespace SkyCD.Plugin.Metadata.IWork.Tests;

/// <summary>
/// End-to-end coverage for the iWork plugin inside the host pipeline: provider selection, host resource
/// budgets, failure isolation, assembly discovery without a manifest, and offline retrieval after the
/// database is reopened with the media gone and the plugin unloaded.
/// </summary>
public sealed class IWorkPluginIntegrationTests : IDisposable
{
    private const string PluginId = "SkyCD.Plugin.IWork";
    private const string MediaRootId = "media-root";
    private const string DocumentEntryId = "file-document-1";

    private readonly TestWorkspace workspace = new();

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    private string databaseDirectory => workspace.Root;

    public IWorkPluginIntegrationTests()
    {
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", databaseDirectory);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);
        workspace.Dispose();
    }

    [Fact]
    public async Task Host_RunsTheMetadataCapabilityForASinglePackage()
    {
        var package = IWorkFixtures.Build();
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".pages", "application/x-iwork-pages-sffpages", package.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(package)));

        var metadata = Assert.Single(results);

        Assert.False(metadata.Failed);
        Assert.Equal("skycd.iwork.metadata", metadata.Schema!.Id);
        Assert.Equal(
            IWorkFixtures.DefaultFormatVersion,
            metadata.Metadata!.Values.Single(value => value.Key == IWorkSchema.FormatVersion).Text);
    }

    [Fact]
    public void Host_ClaimsOnlyIWorkSources()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".pages", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".numbers", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".KEY", "application/x-iwork-keynote-sffkey", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".pdf", "application/pdf", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
    }

    [Fact]
    public async Task MalformedPackage_IsReportedAsAFailedProviderInsteadOfBreakingThePass()
    {
        var package = IWorkFixtures.NotZip();
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".pages", "application/x-iwork-pages-sffpages", package.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(package)));

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.Failed));
        Assert.All(results, result => Assert.Null(result.Metadata));
    }

    [Fact]
    public async Task HostSourceBudget_RejectsADocumentLargerThanTheBudget()
    {
        var package = IWorkFixtures.Build();
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 16));

        var results = await manager.ProcessAsync(
            new MediaItem(".pages", "application/x-iwork-pages-sffpages", package.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(package)));

        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public async Task CapturedMetadata_SurvivesReopen_WithoutThePluginOrTheSource()
    {
        var package = IWorkFixtures.Build(new PackageOptions
        {
            Kind = FixtureDocumentKind.Keynote,
            SlideCount = 3,
            ProducerBuild = "Keynote 14.4"
        });

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, package.Length);
            using var store = MediaCacheStore.Create(database);

            var outcome = await CreateScheduler(store).ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(package)), catalog.GetAll()));

            Assert.True(outcome.Published);
        }

        // Reopened with no plugin registered and no source reachable: only stored bytes remain.
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var entry = catalog.GetAll().Single(document => document.Id == DocumentEntryId);

            var snapshot = new MediaPreviewResolver(store).Resolve(entry);

            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal(PreviewPayloadKind.None, snapshot.PayloadKind);
            Assert.Equal(PluginId, snapshot.ProviderId);

            Assert.Equal("iwork", FieldText(snapshot, IWorkSchema.Container));
            Assert.Equal("keynote", FieldText(snapshot, IWorkSchema.DocumentType));
            Assert.Equal(IWorkFixtures.DefaultFormatVersion, FieldText(snapshot, IWorkSchema.FormatVersion));
            Assert.Equal("Keynote 14.4", FieldText(snapshot, IWorkSchema.ProducerBuild));
            Assert.Equal(IWorkFixtures.DefaultDocumentIdentifier,
                FieldText(snapshot, IWorkSchema.DocumentIdentifier));
            Assert.Equal(3L, FieldInteger(snapshot, IWorkSchema.SlideCount));
            Assert.Equal(2L, FieldInteger(snapshot, IWorkSchema.IwaComponentCount));
            Assert.False(FieldBoolean(snapshot, IWorkSchema.IsPasswordProtected));
        }
    }

    [Fact]
    public async Task CapturedThumbnail_SurvivesReopen_WithoutThePluginOrTheSource()
    {
        var package = IWorkFixtures.Build();
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, package.Length);
            using var store = MediaCacheStore.Create(database);
            var scheduler = new MediaExtractionScheduler(
                new MediaExtensionManager(ProvidersWithPreview()), store);

            var outcome = await scheduler.ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(package)), catalog.GetAll()));

            Assert.True(outcome.Published);
        }

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var entry = catalog.GetAll().Single(document => document.Id == DocumentEntryId);

            var resolver = new MediaPreviewResolver(store);
            var snapshot = resolver.Resolve(entry);

            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
            Assert.Equal(PluginId, snapshot.ProviderId);
            Assert.Equal("image/png", snapshot.Artifact!.ContentType);
            Assert.True(resolver.TryReadPayload(snapshot, out var bytes));
            Assert.Equal(0x89, bytes[0]);
            Assert.Equal((byte)'P', bytes[1]);
        }
    }

    [Fact]
    public async Task CancelledExtraction_PublishesNothing()
    {
        var package = IWorkFixtures.Build();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, package.Length);
        using var store = MediaCacheStore.Create(database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateScheduler(store).ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(package)), catalog.GetAll()),
            progress: null,
            cancelled.Token));

        Assert.Empty(store.GetRecordsForEntry(entry.Id));
    }

    [Fact]
    public void AssemblyDiscovery_FindsTheCapabilities_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(IWorkMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("Apple iWork Document Metadata", plugin.Name);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaPreviewCapability);
        Assert.Equal(2, plugin.Capabilities.Count);
    }

    [Fact]
    public void PackagedPlugin_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = Path.GetDirectoryName(typeof(IWorkMetadataPlugin).Assembly.Location)!;

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
    }

    private static string FieldText(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value as string
        ?? throw new InvalidOperationException($"Field '{key}' is not text.");

    private static long FieldInteger(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value is long value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not an integer.");

    private static bool FieldBoolean(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value is bool value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not a boolean.");

    private static MediaMetadataFieldView Field(EntryPreviewSnapshot snapshot, string key) =>
        snapshot.Fields.Single(field => field.Key == key);

    private static MediaProvider[] Providers() =>
    [
        new MediaProvider(PluginId, "1.0.0", new IWorkMetadataPlugin())
    ];

    private static MediaProvider[] ProvidersWithPreview() =>
    [
        new MediaProvider(PluginId, "1.0.0", new IWorkMetadataPlugin()),
        new MediaProvider(PluginId, "1.0.0", new IWorkPreviewPlugin())
    ];

    private static MediaExtensionManager CreateManager(MediaExecutionLimits? limits = null) =>
        new(Providers(), limits);

    private static MediaExtractionScheduler CreateScheduler(IMediaCacheStore store) =>
        new(new MediaExtensionManager(Providers()), store);

    private Database OpenDatabase() =>
        new("iwork-plugin-tests", new DatabaseConfiguration { Directory = databaseDirectory });

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
            Name = "Documents",
            ParentId = "library",
            Type = CatalogDocumentType.Media
        };

        var entry = new CatalogDocument
        {
            Id = DocumentEntryId,
            Name = "pitch.key",
            ParentId = root.Id,
            Type = CatalogDocumentType.File,

            // The host builds MediaItem.Length from the catalogued size and reads exactly that many bytes.
            Size = entrySize,
            Properties = new PropertiesCollection
            {
                ["FullName"] = @"D:\Documents\pitch.key",
                ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
            }
        };

        repository.Save(root.Id, root);
        repository.Save(entry.Id, entry);
        return entry;
    }

}
