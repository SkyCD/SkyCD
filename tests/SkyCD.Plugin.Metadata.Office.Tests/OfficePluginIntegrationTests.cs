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
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Controls.Preview;
using Xunit;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Tests;

/// <summary>
/// End-to-end coverage for the Microsoft Office plugin inside the host pipeline: provider selection, host
/// resource budgets, failure isolation, assembly discovery without a manifest, and offline retrieval after
/// the database is reopened with the source gone and the plugin unloaded.
/// </summary>
public sealed class OfficePluginIntegrationTests : IDisposable
{
    private const string PluginId = "SkyCD.Plugin.Office";
    private const string MediaRootId = "media-root";
    private const string DocumentEntryId = "file-document-1";

    private readonly string databaseDirectory =
        Path.Combine(Path.GetTempPath(), $"skycd-office-plugin-{Guid.NewGuid():N}");

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public OfficePluginIntegrationTests()
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
    public async Task Host_RunsTheMetadataCapabilityForAnOfficeSource()
    {
        var document = Document();
        var results = await CreateManager().ProcessAsync(
            OfficeMetadataPluginTests.Item(document),
            _ => Task.FromResult<Stream>(new MemoryStream(document)));

        var metadata = Assert.Single(results);
        Assert.False(metadata.Failed);
        Assert.Equal("skycd.office.metadata", metadata.Schema!.Id);
        Assert.Equal("Quarterly Report",
            metadata.Metadata!.Values.Single(value => value.Key == OfficeSchema.Title).Text);
        Assert.Equal(5L,
            metadata.Metadata.Values.Single(value => value.Key == OfficeSchema.PageCount).Integer);
    }

    [Fact]
    public void Host_ClaimsOnlyOfficeSources()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".docx", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".DOCX",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".bin", "application/vnd.ms-excel", 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".xls", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".odt", "application/vnd.oasis.opendocument.text", 10)));
    }

    [Fact]
    public async Task UnreadableDocument_IsReportedAsAFailedProviderInsteadOfBreakingThePass()
    {
        var document = "This is not an Office document."u8.ToArray();
        var results = await CreateManager().ProcessAsync(
            OfficeMetadataPluginTests.Item(document),
            _ => Task.FromResult<Stream>(new MemoryStream(document)));

        var metadata = Assert.Single(results);
        Assert.True(metadata.Failed);
        Assert.Null(metadata.Metadata);
    }

    [Fact]
    public async Task HostSourceBudget_RejectsADocumentLargerThanTheBudget()
    {
        var document = Document();
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 16));

        var results = await manager.ProcessAsync(
            OfficeMetadataPluginTests.Item(document),
            _ => Task.FromResult<Stream>(new MemoryStream(document)));

        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public async Task CapturedMetadata_SurvivesReopen_WithoutThePluginOrTheSource()
    {
        var document = Document();

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, document.Length);
            using var store = MediaCacheStore.Create(database);

            var outcome = await new MediaExtractionScheduler(new MediaExtensionManager(CreateProviders()), store)
                .ProcessAsync(MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(document)), catalog.GetAll()));

            Assert.True(outcome.Published);
        }

        // Reopened with no plugin registered and no source reachable: only stored values remain.
        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            using var store = MediaCacheStore.Create(database);
            var entry = catalog.GetAll().Single(documentEntry => documentEntry.Id == DocumentEntryId);

            var snapshot = new MediaPreviewResolver(store).Resolve(entry);

            Assert.Equal(MediaPreviewState.Ready, snapshot.State);
            Assert.Equal(PreviewPayloadKind.None, snapshot.PayloadKind);
            Assert.Equal(PluginId, snapshot.ProviderId);
            Assert.Equal("ooxml", FieldText(snapshot, OfficeSchema.Container));
            Assert.Equal("word", FieldText(snapshot, OfficeSchema.DocumentType));
            Assert.Equal("Quarterly Report", FieldText(snapshot, OfficeSchema.Title));
            Assert.Equal("A. Writer", FieldText(snapshot, OfficeSchema.Author));
            Assert.Equal("SkyCD Ltd", FieldText(snapshot, OfficeSchema.Company));
            Assert.Equal(OfficeFixtures.Created, Field(snapshot, OfficeSchema.CreatedAt).Value);
            Assert.Equal(1234L, FieldInteger(snapshot, OfficeSchema.WordCount));
        }
    }

    [Fact]
    public async Task CancelledExtraction_PublishesNothing()
    {
        var document = Document();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, document.Length);
        using var store = MediaCacheStore.Create(database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new MediaExtractionScheduler(new MediaExtensionManager(CreateProviders()), store).ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(document)), catalog.GetAll()),
                progress: null,
                cancelled.Token));

        Assert.Empty(store.GetRecordsForEntry(entry.Id));
    }

    [Fact]
    public void AssemblyDiscovery_FindsTheCapability_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(OfficeMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("Microsoft Office Document Metadata", plugin.Name);
        var capability = Assert.Single(plugin.Capabilities);
        Assert.IsAssignableFrom<IMediaMetadataCapability>(capability);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static byte[] Document() =>
        OfficeFixtures.Docx(OfficeFixtures.FullCore(), OfficeFixtures.FullApp());

    private static string FieldText(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value as string
        ?? throw new InvalidOperationException($"Field '{key}' is not text.");

    private static long FieldInteger(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value is long value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not an integer.");

    private static MediaMetadataFieldView Field(EntryPreviewSnapshot snapshot, string key) =>
        snapshot.Fields.Single(field => field.Key == key);

    private static MediaProvider[] CreateProviders() =>
        [new MediaProvider(PluginId, "1.0.0", new OfficeMetadataPlugin())];

    private static MediaExtensionManager CreateManager(MediaExecutionLimits? limits = null) =>
        new(CreateProviders(), limits);

    private Database OpenDatabase() =>
        new("office-plugin-tests", new DatabaseConfiguration { Directory = databaseDirectory });

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
            Name = "report.docx",
            ParentId = root.Id,
            Type = CatalogDocumentType.File,

            // The host builds MediaItem.Length from the catalogued size and reads exactly that many bytes.
            Size = entrySize,
            Properties = new PropertiesCollection
            {
                ["FullName"] = @"D:\Documents\report.docx",
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
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.Office");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the Microsoft Office plugin project directory.");
    }
}
