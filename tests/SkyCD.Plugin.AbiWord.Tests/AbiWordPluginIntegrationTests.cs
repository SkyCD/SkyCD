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
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Controls.Preview;
using Xunit;

namespace SkyCD.Plugin.AbiWord.Tests;

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
    public void AssemblyDiscovery_FindsMetadataOnly_FromAssemblyMetadata()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(AbiWordMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("AbiWord Document Metadata", plugin.Name);
        var capability = Assert.IsAssignableFrom<IMediaMetadataCapability>(Assert.Single(plugin.Capabilities));
        Assert.DoesNotContain(plugin.Capabilities, candidate => candidate is IMediaPreviewCapability);
        Assert.Equal(SchemaId, capability.Descriptor.Schema.Id);
        Assert.Equal(1, capability.Descriptor.Schema.Version);
        Assert.Equal(MediaMetadataType.Text,
            capability.Descriptor.Schema.Fields.Single(field => field.Key == "Title").Type);
        Assert.Equal(MediaMetadataType.Text,
            capability.Descriptor.Schema.Fields.Single(field => field.Key == "Author").Type);
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
        var provider = Assert.Single(CreateManager().ResolveProviders(new MediaItem(extension, contentType, 10)));

        Assert.Equal(PluginId, provider.PluginId);
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
            _ => Task.FromResult<Stream>(new MemoryStream(document))));

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

    [Fact]
    public async Task MalformedXml_IsIsolatedByHost_AndDoesNotBreakTheNextPass()
    {
        var manager = CreateManager();
        var malformed = Encoding.UTF8.GetBytes("<abiword><metadata><m key=\"dc.title\">unfinished");
        var failure = Assert.Single(await manager.ProcessAsync(
            new MediaItem(".abw", "application/x-abiword", malformed.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(malformed))));

        Assert.True(failure.Failed);
        Assert.False(failure.TimedOut);
        Assert.Null(failure.Metadata);
        Assert.Null(failure.Preview);

        var document = Document(compressed: false);
        var success = Assert.Single(await manager.ProcessAsync(
            new MediaItem(".abw", "application/x-abiword", document.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(document))));

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
        var result = Assert.Single(await manager.ProcessAsync(
            new MediaItem(extension, "application/x-abiword", document.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(document))));

        Assert.True(result.Failed);
        Assert.False(result.TimedOut);
        Assert.Null(result.Metadata);
        Assert.Null(result.Preview);
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
            Assert.Equal(PreviewPayloadKind.None, snapshot.PayloadKind);
            Assert.Equal(PluginId, snapshot.ProviderId);
            Assert.False(resolver.TryReadPayload(snapshot, out _));
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

    private static MediaExtensionManager CreateManager(MediaExecutionLimits? limits = null) =>
        new([new MediaProvider(PluginId, "1.0.0", new AbiWordMetadataPlugin())], limits);

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
