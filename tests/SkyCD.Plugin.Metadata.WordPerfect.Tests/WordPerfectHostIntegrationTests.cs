using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using Xunit;
using SkyCD.Plugin.Metadata.WordPerfect.Export;
using SkyCD.Plugin.Metadata.WordPerfect.Models;

namespace SkyCD.Plugin.Metadata.WordPerfect.Tests;

/// <summary>
/// End-to-end coverage inside the host pipeline: extension matching, provider selection, failing extraction
/// classification, and the metadata the catalog cache stores.
/// </summary>
public sealed class WordPerfectHostIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.WordPerfect";

    [Fact]
    public void Host_ClaimsWordPerfectExtensionsAndContentTypeOnly()
    {
        var manager = CreateManager();

        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".wpd", null, 10)).Count);
        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".WPT", null, 10)).Count);
        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".bin", "application/vnd.wordperfect", 10)).Count);
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".zip", null, 10)));
    }

    [Fact]
    public async Task StandardPass_ProducesMetadata()
    {
        var document = WordPerfectFixtures.Document(
            WordPerfectFixtures.TextGroup(17, null, "Pipeline"),
            WordPerfectFixtures.TextGroup(5, null, "A. Writer"));
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".wpd", null, document.Length),
            Open(document),
            selection: MediaProviderSelection.Standard);

        var result = results.Single(candidate => candidate.Metadata is not null);
        Assert.False(result.Failed);
        Assert.Equal(PluginId, result.PluginId);
        Assert.Contains(result.Metadata!.Values, value => value.Key == WordPerfectSchema.Title && value.Text == "Pipeline");
        Assert.Contains(result.Metadata.Values, value => value.Key == WordPerfectSchema.Author && value.Text == "A. Writer");
    }

    [Fact]
    public async Task StandardMetadataPass_RunsTheProvider()
    {
        var document = WordPerfectFixtures.WithoutSummary();
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".wpt", null, document.Length),
            Open(document),
            selection: MediaProviderSelection.StandardMetadata);

        Assert.Single(results);
        Assert.False(results[0].Failed);
    }

    [Fact]
    public async Task OptInMetadataPass_DoesNotSelectTheProvider()
    {
        var document = WordPerfectFixtures.WithoutSummary();
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".wpd", null, document.Length),
            Open(document),
            selection: MediaProviderSelection.OptInMetadata);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ProtectedDocument_IsRecordedAsAFailureRatherThanThrownFromTheHost()
    {
        var document = WordPerfectFixtures.WithSummary([], encryption: 1);
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".wpd", null, document.Length),
            Open(document),
            selection: MediaProviderSelection.Standard);

        var result = results.Single(candidate => candidate.Capability.Contains(
            nameof(WordPerfectMetadataPlugin), StringComparison.Ordinal));
        Assert.True(result.Failed);
        Assert.False(result.TimedOut);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task Scheduler_StoresMetadataThatTheCatalogCanPresent()
    {
        var document = WordPerfectFixtures.Document(
            WordPerfectFixtures.TextGroup(17, null, "Saga Operations"),
            WordPerfectFixtures.TextGroup(5, null, "A. Writer"),
            WordPerfectFixtures.DateGroup(14, 2024, 3, 1));
        var entry = CreateEntry(document);
        using var store = new InMemoryMediaCacheStore();
        var scheduler = new MediaExtractionScheduler(CreateManager(), store);

        var outcome = await scheduler.ProcessAsync(MediaExtractionRequest.ForCatalogEntry(
            entry,
            Open(document),
            new[] { entry }));

        Assert.True(outcome.Published);

        var record = store.GetRecordsForEntry(entry.Id)
            .Single(candidate => candidate.SchemaId == WordPerfectSchema.MetadataSchemaId);
        Assert.Equal(MediaCacheRecordState.Captured, record.State);
        Assert.Contains(record.Metadata, value => value.Key == WordPerfectSchema.Title && value.Text == "Saga Operations");
        Assert.Contains(record.Metadata,
            value => value.Key == WordPerfectSchema.CreatedAt &&
                     value.Timestamp == new DateTimeOffset(2024, 3, 1, 12, 30, 15, TimeSpan.Zero));
    }

    [Fact]
    public void AssemblyDiscovery_FindsBothCapabilitiesWithoutAManifest()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(WordPerfectMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("WordPerfect Document Metadata and Preview", plugin.Name);
        Assert.Equal(2, plugin.Capabilities.Count);
        Assert.Contains(plugin.Capabilities, capability => capability is WordPerfectMetadataPlugin);
        Assert.Contains(plugin.Capabilities, capability => capability is WordPerfectPreviewPlugin);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Host_RunsMetadataAndPreviewForASingleDocument()
    {
        var document = WordPerfectFixtures.DocumentWithBody(
            WordPerfectFixtures.Body(
                WordPerfectFixtures.Text("Body preview text"),
                [0xC7],
                WordPerfectFixtures.Text("Second line")),
            WordPerfectFixtures.TextGroup(17, null, "Integration"));
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".wpd", "application/vnd.wordperfect", document.Length),
            Open(document));

        var metadata = results.Single(result => result.Metadata is not null);
        var preview = results.Single(result => result.Preview is not null);

        Assert.False(metadata.Failed);
        Assert.False(preview.Failed);
        Assert.Equal(WordPerfectSchema.MetadataSchemaId, metadata.Schema!.Id);
        Assert.Equal(WordPerfectSchema.PreviewSchemaId, preview.Schema!.Id);
        Assert.Equal("Integration",
            metadata.Metadata!.Values.Single(value => value.Key == WordPerfectSchema.Title).Text);
        Assert.Equal(RenderedThumbnail, Assert.Single(preview.Preview!.Artifacts).Data.ToArray());
    }

    [Fact]
    public async Task UnsupportedVariant_PreviewIsEmptyButMetadataStillSucceeds()
    {
        var document = WordPerfectFixtures.WithSummary(
            [],
            majorVersion: 0,
            minorVersion: 0,
            body: WordPerfectFixtures.Text("Body of a WP5 document"));
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".wpd", null, document.Length),
            Open(document));

        var metadata = results.Single(result => result.Metadata is not null);
        var preview = results.Single(result => result.Capability.Contains(
            nameof(WordPerfectPreviewPlugin), StringComparison.Ordinal));

        Assert.False(metadata.Failed);
        Assert.False(preview.Failed);
        Assert.Empty(preview.Preview!.Artifacts);
        Assert.Equal("5", metadata.Metadata!.Values
            .Single(value => value.Key == WordPerfectSchema.FormatVersion).Text);
    }

    [Fact]
    public async Task ProtectedDocument_PreviewIsRecordedAsAFailureRatherThanThrownFromTheHost()
    {
        var document = WordPerfectFixtures.WithSummary([], encryption: 1);
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".wpd", null, document.Length),
            Open(document));

        var preview = results.Single(result => result.Capability.Contains(
            nameof(WordPerfectPreviewPlugin), StringComparison.Ordinal));
        Assert.True(preview.Failed);
        Assert.False(preview.TimedOut);
        Assert.Null(preview.Preview);
    }

    [Fact]
    public async Task MalformedBody_StillPublishesMetadataAndAPartialPreview()
    {
        var corrupt = WordPerfectFixtures.VariableGroup(0xD3, 0, WordPerfectFixtures.Text("hidden"));
        corrupt[2] = 0xFF;
        corrupt[3] = 0xFF;
        var document = WordPerfectFixtures.DocumentWithBody(
            WordPerfectFixtures.Body(WordPerfectFixtures.Text("Recovered"), corrupt),
            WordPerfectFixtures.TextGroup(17, null, "Damaged"));
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".wpd", null, document.Length),
            Open(document));

        var metadata = results.Single(result => result.Metadata is not null);
        var preview = results.Single(result => result.Preview is not null);

        Assert.False(metadata.Failed);
        Assert.False(preview.Failed);
        Assert.Equal("Damaged", metadata.Metadata!.Values.Single(value => value.Key == WordPerfectSchema.Title).Text);
        Assert.Equal(RenderedThumbnail, Assert.Single(preview.Preview!.Artifacts).Data.ToArray());
    }

    [Fact]
    public async Task NoRenderer_StillStoresMetadataWithNoArtifact()
    {
        var document = WordPerfectFixtures.DocumentWithBody(
            WordPerfectFixtures.Text("Body text"),
            WordPerfectFixtures.TextGroup(17, null, "Metadata only"));
        var manager = CreateManager(rendererAvailable: false);

        var results = await manager.ProcessAsync(
            new MediaItem(".wpd", null, document.Length),
            Open(document));

        var preview = results.Single(result => result.Capability.Contains(
            nameof(WordPerfectPreviewPlugin), StringComparison.Ordinal));
        Assert.False(preview.Failed);
        Assert.Empty(preview.Preview!.Artifacts);

        var metadata = results.Single(result => result.Metadata is not null);
        Assert.False(metadata.Failed);
        Assert.Equal("Metadata only",
            metadata.Metadata!.Values.Single(value => value.Key == WordPerfectSchema.Title).Text);
    }

    private static MediaExtensionManager CreateManager(bool rendererAvailable = true) =>
        new(new[]
        {
            new MediaProvider(PluginId, "1.0.0", new WordPerfectMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new WordPerfectPreviewPlugin(
                WordPerfectPluginOptions.Default,
                new StubRenderer(rendererAvailable)))
        });

    private static readonly byte[] RenderedThumbnail =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    /// <summary>Stands in for the Skia renderer so host tests assert plumbing, not rasterization.</summary>
    private sealed class StubRenderer(bool available) : IWordPerfectThumbnailRenderer
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

    private static Func<CancellationToken, Task<Stream>> Open(byte[] document) =>
        _ => Task.FromResult<Stream>(new MemoryStream(document, writable: false));

    private static CatalogDocument CreateEntry(byte[] document) => new()
    {
        Id = "wordperfect-entry-1",
        Name = "report.wpd",
        ParentId = "documents-root",
        Type = CatalogDocumentType.File,
        Size = document.Length,
        Properties = new PropertiesCollection
        {
            ["FullName"] = @"D:\Documents\report.wpd",
            ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
        }
    };

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.WordPerfect");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the WordPerfect plugin project directory.");
    }
}
