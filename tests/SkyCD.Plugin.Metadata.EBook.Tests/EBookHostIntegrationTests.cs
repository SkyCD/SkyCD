using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Controls.Preview;
using Xunit;
using SkyCD.Plugin.Metadata.EBook.Models;
using SkyCD.Plugin.Metadata.EBook.Parsing;

namespace SkyCD.Plugin.Metadata.EBook.Tests;

/// <summary>
/// End-to-end coverage inside the host pipeline: extension matching, provider selection, settings-driven
/// behavior, index-time cover storage, and the catalog effects the schema roles ask the host to apply.
/// </summary>
public sealed class EBookHostIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.EBook";

    [Fact]
    public void Host_ClaimsTheConfiguredEbookExtensionsAndContentTypes()
    {
        var manager = CreateManager(EBookFixtures.Settings(extractCovers: true));

        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".epub", null, 10)).Count);
        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".FB2", null, 10)).Count);
        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".azw3", null, 10)).Count);
        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".ibook", null, 10)).Count);
        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".bin", "application/epub+zip", 10)).Count);
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".zip", null, 10)));
    }

    [Fact]
    public void DisabledCollection_LeavesNoProvidersForEbooks()
    {
        var manager = CreateManager(EBookFixtures.Settings(collectionEnabled: false, extractCovers: true));

        Assert.Empty(manager.ResolveProviders(new MediaItem(".epub", null, 10)));
    }

    [Fact]
    public async Task IndexingPass_RunsMetadataAndTheIndexTimeCover()
    {
        var book = EBookFixtures.Epub(cover: EBookFixtures.Jpeg(600, 900, SKColors.Teal));
        var manager = CreateManager(EBookFixtures.Settings(extractCovers: true));

        Assert.True(manager.HasIndexingPreviewProviders);
        var results = await manager.ProcessAsync(
            new MediaItem(".epub", null, book.Length),
            OpenBook(book),
            selection: MediaProviderSelection.StandardMetadata);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.False(result.Failed));
        var preview = results.Single(result => result.Preview is not null);
        using var cover = EBookFixtures.Decode(Assert.Single(preview.Preview!.Artifacts).Data);
        Assert.True(Math.Max(cover.Width, cover.Height) <= EBookSettings.DefaultCoverSize);
    }

    [Fact]
    public async Task Scheduler_StoresMetadataAndCoverThatTheCatalogCanPresent()
    {
        var book = EBookFixtures.Epub(cover: EBookFixtures.Jpeg(600, 900, SKColors.Teal));
        var settings = EBookFixtures.Settings(
            nameSource: EBookNameSource.Author,
            tagAuthors: true,
            extractCovers: true,
            coverSize: "256");
        var entry = CreateEntry(book);
        using var store = new InMemoryMediaCacheStore();
        var scheduler = new MediaExtractionScheduler(CreateManager(settings), store);

        var outcome = await scheduler.ProcessAsync(MediaExtractionRequest.ForCatalogEntry(
            entry,
            OpenBook(book),
            new[] { entry }));

        Assert.True(outcome.Published);

        var records = store.GetRecordsForEntry(entry.Id);
        Assert.Equal(2, records.Count);
        var metadata = records.Single(record => record.SchemaId == EBookSchema.MetadataSchemaId);
        Assert.Equal(MediaCacheRecordState.Captured, metadata.State);
        Assert.Equal("The Long Way",
            metadata.Metadata.Single(value => value.Key == EBookSchema.Title).Text);
        Assert.Equal("Becky Chambers; Ann Leckie",
            metadata.Metadata.Single(value => value.Key == EBookSchema.Authors).Text);

        var resolver = new MediaPreviewResolver(store);
        var snapshot = resolver.Resolve(entry);
        Assert.Equal(MediaPreviewState.Ready, snapshot.State);
        Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
        Assert.Equal(PluginId, snapshot.ProviderId);
        Assert.True(resolver.TryReadPayload(snapshot, out var payload));
        using var cover = EBookFixtures.Decode(payload);
        Assert.True(Math.Max(cover.Width, cover.Height) <= 256);

        var displayName = MediaMetadataCatalogEffects.ResolveDisplayName(entry, records);
        Assert.Equal("Becky Chambers; Ann Leckie", displayName);
        var tagNames = MediaMetadataCatalogEffects.ResolveTagNames(records);
        Assert.Equal(["Becky Chambers", "Ann Leckie"], tagNames);
    }

    [Fact]
    public void SettingsRevision_ChangesWithThePersistedOptions()
    {
        var item = new MediaItem(".epub", null, 10);
        var authorManager = CreateManager(
            EBookFixtures.Settings(nameSource: EBookNameSource.Author, extractCovers: true));
        var titleManager = CreateManager(
            EBookFixtures.Settings(nameSource: EBookNameSource.Title, extractCovers: true));

        var author = authorManager.ResolveProviders(item);
        var title = titleManager.ResolveProviders(item);

        Assert.Equal(2, author.Count);
        Assert.All(author, identity => Assert.NotEmpty(identity.SettingsRevision));
        Assert.NotEqual(author[0].SettingsRevision, title[0].SettingsRevision);
    }

    [Fact]
    public void AssemblyDiscovery_FindsAllThreeCapabilitiesWithoutAManifest()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(EBookMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("eBook Metadata and Covers", plugin.Name);
        Assert.Equal(3, plugin.Capabilities.Count);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaPreviewCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IPluginConfigurationCapability);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static MediaExtensionManager CreateManager(EBookSettings settings) =>
        new(new[]
        {
            new MediaProvider(PluginId, "1.0.0", new EBookMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new EBookCoverPlugin())
        }, settingsSource: _ => settings.ToDictionary());

    private static Func<CancellationToken, Task<Stream>> OpenBook(byte[] book) =>
        _ => Task.FromResult<Stream>(new MemoryStream(book, writable: false));

    private static CatalogDocument CreateEntry(byte[] book) => new()
    {
        Id = "ebook-entry-1",
        Name = "the-long-way.epub",
        ParentId = "books-root",
        Type = CatalogDocumentType.File,
        Size = book.Length,
        Properties = new PropertiesCollection
        {
            ["FullName"] = @"D:\Books\the-long-way.epub",
            ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
        }
    };

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.EBook");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the eBook plugin project directory.");
    }
}
