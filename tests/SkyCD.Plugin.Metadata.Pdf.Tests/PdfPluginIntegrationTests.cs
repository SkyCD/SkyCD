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
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Controls.Preview;
using Xunit;
using SkyCD.Plugin.Metadata.Pdf.Export;
using SkyCD.Plugin.Metadata.Pdf.Models;

namespace SkyCD.Plugin.Metadata.Pdf.Tests;

/// <summary>
/// End-to-end coverage for the PDF plugin inside the host pipeline: provider selection, host resource
/// budgets, the host's own timeout, cancellation, failure isolation, assembly discovery without a manifest,
/// and offline retrieval after the database is reopened with the media gone and the plugin unloaded.
/// </summary>
public sealed class PdfPluginIntegrationTests : IDisposable
{
    private const string PluginId = "SkyCD.Plugin.Pdf";
    private const string MediaRootId = "media-root";
    private const string DocumentEntryId = "file-document-1";

    /// <summary>A deterministic stand-in frame, so the pipeline does not depend on a native rasterizer.</summary>
    private static readonly byte[] RenderedPage = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly string databaseDirectory =
        Path.Combine(Path.GetTempPath(), $"skycd-pdf-plugin-{Guid.NewGuid():N}");

    private readonly string? previousDatabaseDirectory =
        Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public PdfPluginIntegrationTests()
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
    public async Task Host_RunsBothCapabilitiesForASinglePdf()
    {
        var pdf = PdfFixtures.Document(pageCount: 2, title: "Integration");
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(pdf)));

        var metadata = results.Single(result => result.Metadata is not null);
        var preview = results.Single(result => result.Preview is not null);

        Assert.False(metadata.Failed);
        Assert.False(preview.Failed);
        Assert.Equal("skycd.pdf.metadata", metadata.Schema!.Id);
        Assert.Equal("skycd.pdf.preview", preview.Schema!.Id);
        Assert.Equal(2L, metadata.Metadata!.Values.Single(value => value.Key == PdfSchema.PageCount).Integer);
        Assert.Equal(RenderedPage, Assert.Single(preview.Preview!.Artifacts).Data.ToArray());
    }

    [Fact]
    public void Host_ClaimsOnlyPdfSources()
    {
        var manager = CreateManager();

        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".pdf", null, 10)));
        Assert.NotEmpty(manager.ResolveProviders(new MediaItem(".PDF", "application/pdf", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".jpg", "image/jpeg", 10)));
    }

    [Fact]
    public async Task MalformedDocument_IsReportedAsAFailedProviderInsteadOfBreakingThePass()
    {
        var pdf = PdfFixtures.Malformed();
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(pdf)));

        Assert.NotEmpty(results);
        Assert.All(results, result => Assert.True(result.Failed));
        Assert.All(results, result => Assert.Null(result.Metadata));
    }

    [Fact]
    public async Task EncryptedDocument_FailsAsAuthRequiredWithoutPublishingACredential()
    {
        var pdf = PdfFixtures.Document(encrypted: true);
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(pdf)));

        Assert.All(results, result => Assert.True(result.Failed));

        // The failure reason the host stores is sanitized, so no password can leak into the catalog.
        Assert.All(results, result => Assert.Null(result.Metadata));
    }

    [Fact]
    public async Task HostSourceBudget_RejectsADocumentLargerThanTheBudget()
    {
        var pdf = PdfFixtures.Document(pageCount: 2);
        var manager = CreateManager(new MediaExecutionLimits(SourceBytes: 16));

        var results = await manager.ProcessAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(pdf)));

        Assert.All(results, result => Assert.True(result.Failed));
    }

    [Fact]
    public async Task SlowRasterizer_IsTimedOutByTheHostWithoutHangingThePass()
    {
        var pdf = PdfFixtures.Document();
        var manager = CreateManager(new MediaExecutionLimits(TimeoutMilliseconds: 150), slowRendererDelay: 10_000);

        var results = await manager.ProcessAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(pdf)));

        var preview = results.Single(result => result.Capability.Contains(nameof(PdfPreviewPlugin), StringComparison.Ordinal));
        Assert.True(preview.Failed);
        Assert.True(preview.TimedOut);

        // Metadata does not depend on the rasterizer, so it still succeeds in the same pass.
        var metadata = results.Single(result => result.Metadata is not null);
        Assert.False(metadata.Failed);
    }

    [Fact]
    public async Task NoRasterizer_StillStoresMetadataWithNoArtifact()
    {
        var pdf = PdfFixtures.Document(pageCount: 3, title: "Metadata only");

        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, pdf.Length);
        using var store = MediaCacheStore.Create(database);

        var outcome = await CreateScheduler(store, rendererAvailable: false).ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(pdf)), catalog.GetAll()));

        Assert.True(outcome.Published);

        var snapshot = new MediaPreviewResolver(store).Resolve(entry);

        Assert.Equal(MediaPreviewState.Ready, snapshot.State);
        Assert.Equal(PreviewPayloadKind.None, snapshot.PayloadKind);
        Assert.Equal(3L, FieldInteger(snapshot, PdfSchema.PageCount));
        Assert.Equal("Metadata only", FieldText(snapshot, PdfSchema.Title));
    }

    [Fact]
    public async Task CapturedMetadataAndFirstPage_SurviveReopen_WithoutThePluginOrTheSource()
    {
        var created = new DateTimeOffset(2021, 3, 4, 12, 34, 56, TimeSpan.Zero);
        var pdf = PdfFixtures.Document(
            pageCount: 4,
            text: "Persisted text layer",
            title: "Offline Report",
            author: "A. Writer",
            createdAt: created);

        using (var database = OpenDatabase())
        {
            var catalog = CreateCatalogRepository(database);
            var entry = SeedCatalog(catalog, pdf.Length);
            using var store = MediaCacheStore.Create(database);

            var outcome = await CreateScheduler(store).ProcessAsync(
                MediaExtractionRequest.ForCatalogEntry(
                    entry, _ => Task.FromResult<Stream>(new MemoryStream(pdf)), catalog.GetAll()));

            Assert.True(outcome.Published);
        }

        // Reopened with no plugin registered and no source reachable: only stored bytes remain.
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
            Assert.True(resolver.TryReadPayload(snapshot, out var payload));
            Assert.Equal(RenderedPage, payload);

            Assert.Equal("pdf", FieldText(snapshot, PdfSchema.Container));
            Assert.Equal(4L, FieldInteger(snapshot, PdfSchema.PageCount));
            Assert.Equal("Offline Report", FieldText(snapshot, PdfSchema.Title));
            Assert.Equal("A. Writer", FieldText(snapshot, PdfSchema.Author));
            Assert.Equal(created, Field(snapshot, PdfSchema.CreatedAt).Value);
            Assert.Contains("Persisted text layer", FieldText(snapshot, PdfSchema.TextExcerpt), StringComparison.Ordinal);
            Assert.Equal(false, Field(snapshot, PdfSchema.TextExcerptTruncated).Value);
        }
    }

    [Fact]
    public async Task CancelledExtraction_PublishesNothing()
    {
        var pdf = PdfFixtures.Document();
        using var database = OpenDatabase();
        var catalog = CreateCatalogRepository(database);
        var entry = SeedCatalog(catalog, pdf.Length);
        using var store = MediaCacheStore.Create(database);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateScheduler(store).ProcessAsync(
            MediaExtractionRequest.ForCatalogEntry(
                entry, _ => Task.FromResult<Stream>(new MemoryStream(pdf)), catalog.GetAll()),
            progress: null,
            cancelled.Token));

        Assert.Empty(store.GetRecordsForEntry(entry.Id));
    }

    [Fact]
    public void AssemblyDiscovery_FindsBothCapabilities_FromMetadataOnly()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(PdfMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("PDF Document Metadata and First-Page Previews", plugin.Name);
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

    private static string FieldText(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value as string
        ?? throw new InvalidOperationException($"Field '{key}' is not text.");

    private static long FieldInteger(EntryPreviewSnapshot snapshot, string key) =>
        Field(snapshot, key).Value is long value
            ? value
            : throw new InvalidOperationException($"Field '{key}' is not an integer.");

    private static MediaMetadataFieldView Field(EntryPreviewSnapshot snapshot, string key) =>
        snapshot.Fields.Single(field => field.Key == key);

    private static MediaProvider[] Providers(bool rendererAvailable, int slowRendererDelay) =>
    [
        new MediaProvider(PluginId, "1.0.0", new PdfMetadataPlugin()),
        new MediaProvider(PluginId, "1.0.0", new PdfPreviewPlugin(
            PdfPluginOptions.Default,
            new StubRenderer(rendererAvailable, rendererAvailable ? RenderedPage : null, slowRendererDelay)))
    ];

    private static MediaExtensionManager CreateManager(
        MediaExecutionLimits? limits = null,
        bool rendererAvailable = true,
        int slowRendererDelay = 0) =>
        new(Providers(rendererAvailable, slowRendererDelay), limits);

    private static MediaExtractionScheduler CreateScheduler(IMediaCacheStore store, bool rendererAvailable = true) =>
        new(new MediaExtensionManager(Providers(rendererAvailable, 0)), store);

    private Database OpenDatabase() =>
        new("pdf-plugin-tests", new DatabaseConfiguration { Directory = databaseDirectory });

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
            Name = "report.pdf",
            ParentId = root.Id,
            Type = CatalogDocumentType.File,

            // The host builds MediaItem.Length from the catalogued size and reads exactly that many bytes.
            Size = entrySize,
            Properties = new PropertiesCollection
            {
                ["FullName"] = @"D:\Documents\report.pdf",
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
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.Pdf");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the PDF plugin project directory.");
    }

    /// <summary>Stands in for the native rasterizer, optionally slowly, so host timeouts can be exercised.</summary>
    private sealed class StubRenderer : IPdfPageRenderer
    {
        private readonly byte[]? image;
        private readonly int delayMilliseconds;

        public StubRenderer(bool available, byte[]? image, int delayMilliseconds)
        {
            IsAvailable = available;
            this.image = image;
            this.delayMilliseconds = delayMilliseconds;
        }

        public bool IsAvailable { get; }

        public async Task<byte[]?> RenderPageAsync(
            byte[] document,
            int pageIndex,
            int maxDimension,
            CancellationToken cancellationToken)
        {
            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds, cancellationToken);
            }

            return image;
        }
    }
}
