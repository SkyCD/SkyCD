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

        Assert.Single(manager.ResolveProviders(new MediaItem(".wpd", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".WPT", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".bin", "application/vnd.wordperfect", 10)));
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

        var result = Assert.Single(results);
        Assert.False(result.Failed);
        Assert.Equal(PluginId, result.PluginId);
        Assert.NotNull(result.Metadata);
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

        var result = Assert.Single(results);
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

        var records = store.GetRecordsForEntry(entry.Id);
        var record = Assert.Single(records);
        Assert.Equal(WordPerfectSchema.MetadataSchemaId, record.SchemaId);
        Assert.Equal(MediaCacheRecordState.Captured, record.State);
        Assert.Contains(record.Metadata, value => value.Key == WordPerfectSchema.Title && value.Text == "Saga Operations");
        Assert.Contains(record.Metadata,
            value => value.Key == WordPerfectSchema.CreatedAt &&
                     value.Timestamp == new DateTimeOffset(2024, 3, 1, 12, 30, 15, TimeSpan.Zero));
    }

    [Fact]
    public void AssemblyDiscovery_FindsTheMetadataCapabilityWithoutAManifest()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(WordPerfectMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("WordPerfect Document Metadata", plugin.Name);
        var capability = Assert.Single(plugin.Capabilities);
        Assert.IsType<WordPerfectMetadataPlugin>(capability);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static MediaExtensionManager CreateManager() =>
        new(new[] { new MediaProvider(PluginId, "1.0.0", new WordPerfectMetadataPlugin()) });

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
