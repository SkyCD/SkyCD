using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Metadata.AbiWord.Export;
using SkyCD.Plugin.Metadata.AbiWord.Models;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Controls.Preview;
using Xunit;

namespace SkyCD.Plugin.Metadata.AbiWord.Tests;

/// <summary>Exercises discovery, host execution and persisted offline metadata with real AbiWord XML and gzip.</summary>
public sealed class AbiWordPluginIntegrationTests : IDisposable
{
    private const string PluginId = "SkyCD.Plugin.AbiWord";
    private const string SchemaId = "skycd.abiword.metadata";
    private const string MediaRootId = "media-root";
    private const string DocumentEntryId = "file-document-1";
    private const string DocumentXml = """
        <abiword xmlns="http://www.abisource.com/awml.dtd"><metadata><m key="dc.title">Offline report</m><m key="dc.creator">A. Writer</m></metadata><section><p>body</p></section></abiword>
        """;

    private readonly string databaseDirectory =
        Path.Combine(Path.GetTempPath(), $"skycd-abiword-plugin-{Guid.NewGuid():N}");

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public AbiWordPluginIntegrationTests()
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
    public void AssemblyDiscovery_FindsMetadataAndPreview_FromAssemblyMetadata()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(AbiWordMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("AbiWord Document Metadata and Preview", plugin.Name);
        Assert.Equal(2, plugin.Capabilities.Count);
        var capability = Assert.Single(plugin.Capabilities.OfType<IMediaMetadataCapability>());
        var preview = Assert.Single(plugin.Capabilities.OfType<IMediaPreviewCapability>());
        Assert.Equal(SchemaId, capability.Descriptor.Schema.Id);
        Assert.Equal(1, capability.Descriptor.Schema.Version);
        Assert.Equal(MediaMetadataType.Text,
            capability.Descriptor.Schema.Fields.Single(field => field.Key == "Title").Type);
        Assert.Equal(MediaMetadataType.Text,
            capability.Descriptor.Schema.Fields.Single(field => field.Key == "Author").Type);
        Assert.Equal(AbiWordSchema.PreviewSchemaId, preview.Descriptor.Schema.Id);
        Assert.Equal(capability.Descriptor.Extensions, preview.Descriptor.Extensions);
        Assert.Equal(capability.Descriptor.ContentTypes, preview.Descriptor.ContentTypes);
    }

    [Theory]
    [InlineData(".abw", null)]
    [InlineData(".ABW", null)]
    [InlineData(".zabw", null)]
    [InlineData(".ZaBw", null)]
    [InlineData(".unknown", "application/x-abiword")]
    [InlineData(".unknown", "APPLICATION/X-ABIWORD")]
    public void Host_SelectsAbiWordByExtensionOrMime(string extension, string? contentType)
    {
        var providers = CreateManager().ResolveProviders(new MediaItem(extension, contentType, 10));

        Assert.Equal(2, providers.Count);
        Assert.All(providers, provider => Assert.Equal(PluginId, provider.PluginId));
        Assert.Single(providers, provider => provider.Capability == typeof(AbiWordMetadataPlugin).FullName);
        Assert.Single(providers, provider => provider.Capability == typeof(AbiWordPreviewPlugin).FullName);
    }

    [Fact]
    public void Host_RejectsPdf()
    {
        Assert.Empty(CreateManager().ResolveProviders(new MediaItem(".pdf", "application/pdf", 10)));
    }

    [Theory]
    [InlineData(".abw", false)]
    [InlineData(".zabw", true)]
    [InlineData(".abw", true)]
    [InlineData(".zabw", false)]
    public async Task Host_ReadsMetadata_UsingGzipMagicRatherThanExtension(string extension, bool compressed)
    {
        var document = Document(compressed);
        var result = Assert.Single(await CreateManager().ProcessAsync(
            new MediaItem(extension, "application/x-abiword", document.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(document)),
            selection: MediaProviderSelection.StandardMetadata));

        Assert.False(result.Failed);
        Assert.False(result.TimedOut);
        Assert.Equal(SchemaId, result.Schema!.Id);
        Assert.Equal(1, result.Schema.Version);
        Assert.Null(result.Preview);
        Assert.NotNull(result.Metadata);
        var title = result.Metadata.Values.Single(value => value.Key == "Title");
        var author = result.Metadata.Values.Single(value => value.Key == "Author");
        Assert.Equal(MediaMetadataType.Text, title.Type);
        Assert.Equal("Offline report", title.Text);
        Assert.Equal(MediaMetadataType.Text, author.Type);
        Assert.Equal("A. Writer", author.Text);
    }

    [Theory]
    [InlineData(".abw", false)]
    [InlineData(".zabw", true)]
    public async Task Host_RunsMetadataAndPreviewForASingleDocument(string extension, bool compressed)
    {
        var document = Document(compressed);
        var results = await CreateManager().ProcessAsync(
            new MediaItem(extension, "application/x-abiword", document.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(document)));

        var metadata = results.Single(result => result.Metadata is not null);
        var preview = results.Single(result => result.Preview is not null);

        Assert.False(metadata.Failed);
        Assert.False(preview.Failed);
        Assert.Equal(SchemaId, metadata.Schema!.Id);
        Assert.Equal(AbiWordSchema.PreviewSchemaId, preview.Schema!.Id);
        Assert.Equal("Offline report",
            metadata.Metadata!.Values.Single(value => value.Key == "Title").Text);
        Assert.Equal(RenderedThumbnail, Assert.Single(preview.Preview!.Artifacts).Data.ToArray());
    }

    [Theory]
    [InlineData(".abw", false)]
    [InlineData(".zabw", true)]
    public async Task Host_MissingBodyPreviewIsEmptyButMetadataStillSucceeds(string extension, bool compressed)
    {
        var xml = Encoding.UTF8.GetBytes(
            "<abiword xmlns=\"http://www.abisource.com/awml.dtd\"><metadata><m key=\"dc.title\">Title only</m></metadata></abiword>");
        var document = compressed ? AbiWordFixtures.Gzip(xml) : xml;
        var results = await CreateManager().ProcessAsync(
            new MediaItem(extension, "application/x-abiword", document.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(document)));

        var metadata = results.Single(result => result.Metadata is not null);
        var preview = results.Single(result => result.Capability.Contains(
            nameof(AbiWordPreviewPlugin), StringComparison.Ordinal));

        Assert.False(metadata.Failed);
        Assert.False(preview.Failed);
        Assert.Empty(preview.Preview!.Artifacts);
        Assert.Equal("Title only", metadata.Metadata!.Values.Single(value => value.Key == "Title").Text);
    }

    [Fact]
    public async Task Host_NoRenderer_StillStoresMetadataWithNoArtifact()
    {
        var document = Document(compressed: false);
        var results = await CreateManager(rendererAvailable: false).ProcessAsync(
            new MediaItem(".abw", "application/x-abiword", document.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(document)));

        var preview = results.Single(result => result.Capability.Contains(
            nameof(AbiWordPreviewPlugin), StringComparison.Ordinal));
        Assert.False(preview.Failed);
        Assert.Empty(preview.Preview!.Artifacts);

        var metadata = results.Single(result => result.Metadata is not null);
        Assert.Equal("Offline report", metadata.Metadata!.Values.Single(value => value.Key == "Title").Text);
    }

    [Fact]
    public async Task Scheduler_StoresMetadataAndThumbnail_presentedOffline()
    {
        var document = Document(compressed: false);
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, document.Length, ".abw");
        using var store = MediaCacheStore.Create(database);

        var outcome = await CreateScheduler(store).ProcessAsync(MediaExtractionRequest.ForCatalogEntry(
            entry, _ => Task.FromResult<Stream>(new MemoryStream(document)), catalog.GetAll()));

        Assert.True(outcome.Published);

        var records = store.GetRecordsForEntry(entry.Id);
        Assert.Equal(2, records.Count);
        var metadata = Assert.Single(records, record => record.SchemaId == SchemaId);
        var thumbnail = Assert.Single(records, record => record.SchemaId == AbiWordSchema.PreviewSchemaId);
        Assert.Equal(MediaCacheRecordState.Captured, metadata.State);
        Assert.Equal(MediaCacheRecordState.Captured, thumbnail.State);
        Assert.Contains(metadata.Metadata, value => value.Key == "Title");
        Assert.True(store.TryReadArtifact(thumbnail.Id, Assert.Single(thumbnail.Artifacts).Key, out var png));
        Assert.Equal(RenderedThumbnail, png);
    }

    [Fact]
    public async Task MalformedXml_PublishesNoMetadataAndAnEmptyPreview_AndDoesNotBreakTheNextPass()
    {
        var manager = CreateManager();
        var malformed = Encoding.UTF8.GetBytes("<abiword><metadata><m key=\"dc.title\">unfinished");
        var failed = await manager.ProcessAsync(
            new MediaItem(".abw", "application/x-abiword", malformed.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(malformed)));

        var metadataFailure = Assert.Single(failed, result => result.Capability.Contains(
            nameof(AbiWordMetadataPlugin), StringComparison.Ordinal));
        var previewResult = Assert.Single(failed, result => result.Capability.Contains(
            nameof(AbiWordPreviewPlugin), StringComparison.Ordinal));
        Assert.True(metadataFailure.Failed);
        Assert.False(metadataFailure.TimedOut);
        Assert.Null(metadataFailure.Metadata);
        Assert.Null(metadataFailure.Preview);
        Assert.False(previewResult.Failed);
        Assert.Empty(previewResult.Preview!.Artifacts);

        var document = Document(compressed: false);
        var success = Assert.Single(await manager.ProcessAsync(
            new MediaItem(".abw", "application/x-abiword", document.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(document)),
            selection: MediaProviderSelection.StandardMetadata));

        Assert.False(success.Failed);
        Assert.Equal("Offline report", success.Metadata!.Values.Single(value => value.Key == "Title").Text);
    }

    [Theory]
    [InlineData(".abw", false)]
    [InlineData(".zabw", true)]
    public async Task HostSourceBudget_RejectsOversizedSource_WithoutMetadata(string extension, bool compressed)
    {
        var document = Document(compressed);
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 16));
        var results = await manager.ProcessAsync(
            new MediaItem(extension, "application/x-abiword", document.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(document)));

        Assert.Equal(2, results.Count);
        Assert.All(results, result =>
        {
            Assert.True(result.Failed);
            Assert.False(result.TimedOut);
            Assert.Null(result.Metadata);
            Assert.Null(result.Preview);
        });
    }

    [Theory]
    [InlineData(".abw", false)]
    [InlineData(".zabw", true)]
    public async Task CapturedMetadata_SurvivesReopen_WithoutPluginOrSource(string extension, bool compressed)
    {
        var document = Document(compressed);
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, document.Length, extension);
            using var store = MediaCacheStore.Create(database);

            var outcome = await CreateScheduler(store).ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(document)), catalog.GetAll()));

            Assert.True(outcome.Published);
            Assert.NotEmpty(store.GetRecordsForEntry(entry.Id));
        }

        // No scheduler, plugin registration or source opener exists in the reopened read path.
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var entry = catalog.GetAll().Single(candidate => candidate.Id == DocumentEntryId);
            var resolver = new MediaPreviewResolver(store);
            var snapshot = resolver.Resolve(entry);

            Assert.Equal("report" + extension, entry.Name);
            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
            Assert.Equal(PluginId, snapshot.ProviderId);
            Assert.True(resolver.TryReadPayload(snapshot, out var png));
            Assert.Equal(RenderedThumbnail, png);
            Assert.Equal("Offline report", FieldText(snapshot, "Title"));
            Assert.Equal("A. Writer", FieldText(snapshot, "Author"));
        }
    }

    [Fact]
    public async Task CancelledExtraction_PublishesNothing()
    {
        var document = Document(compressed: false);
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, document.Length, ".abw");
        using var store = MediaCacheStore.Create(database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateScheduler(store).ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(document)), catalog.GetAll()),
            progress: null,
            cancelled.Token));

        Assert.Empty(store.GetRecordsForEntry(entry.Id));
    }

    private static byte[] Document(bool compressed)
    {
        var bytes = Encoding.UTF8.GetBytes(DocumentXml);
        if (!compressed)
        {
            return bytes;
        }

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes);
        }

        return output.ToArray();
    }

    private static string FieldText(EntryPreviewSnapshot snapshot, string key) =>
        Assert.IsType<string>(snapshot.Fields.Single(field => field.Key == key).Value);

    private static MediaExtensionManager CreateManager(
        MediaExecutionLimits? limits = null, bool rendererAvailable = true) =>
        new(new[]
        {
            new MediaProvider(PluginId, "1.0.0", new AbiWordMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new AbiWordPreviewPlugin(
                AbiWordPluginOptions.Default,
                new StubRenderer(rendererAvailable)))
        }, limits);

    private static readonly byte[] RenderedThumbnail =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    /// <summary>Stands in for the Skia renderer so host tests assert plumbing, not rasterization.</summary>
    private sealed class StubRenderer(bool available) : IAbiWordThumbnailRenderer
    {
        public bool IsAvailable => available;

        public Task<byte[]?> RenderAsync(
            string text,
            int maxDimension,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<byte[]?>(available ? RenderedThumbnail : null);
        }
    }

    private static MediaExtractionScheduler CreateScheduler(IMediaCacheStore store) => new(CreateManager(), store);

    private Database OpenDatabase() =>
        new("abiword-plugin-tests", new DatabaseConfiguration { Directory = databaseDirectory });

    private static CatalogDocumentRepository CreateCatalogRepository(Database database)
    {
        var collection = database.GetCollection("catalog", Collection.DefaultScopeName)
                         ?? database.CreateCollection("catalog", Collection.DefaultScopeName);
        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), "catalog", collection);
        return repository;
    }

    private static CatalogDocument SeedCatalog(CatalogDocumentRepository repository, long entrySize, string extension)
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
            Name = "report" + extension,
            ParentId = root.Id,
            Type = CatalogDocumentType.File,
            Size = entrySize,
            Properties = new PropertiesCollection
            {
                ["FullName"] = @"D:\Documents\report" + extension,
                ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
            }
        };

        repository.Save(root.Id, root);
        repository.Save(entry.Id, entry);
        return entry;
    }
}
