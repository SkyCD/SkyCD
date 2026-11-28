using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Controls.Preview;
using Xunit;
using SkyCD.Plugin.Metadata.EComic.Models;

namespace SkyCD.Plugin.Metadata.EComic.Tests;

/// <summary>
/// End-to-end coverage inside the host pipeline: extension matching, provider selection, settings-driven
/// behavior, index-time cover storage, and the catalog effects the schema roles ask the host to apply.
/// </summary>
public sealed class EComicHostIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.EComic";

    [Fact]
    public void Host_ClaimsTheConfiguredComicExtensionsOnly()
    {
        var manager = CreateManager(EComicFixtures.Settings(extractCovers: true));

        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".cbz", null, 10)).Count);
        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".CB7", null, 10)).Count);
        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".bin", "application/x-cbr", 10)).Count);
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".zip", null, 10)));
    }

    [Fact]
    public void DisabledCollection_LeavesNoProvidersForComicFiles()
    {
        var manager = CreateManager(EComicFixtures.Settings(collectionEnabled: false, extractCovers: true));

        Assert.Empty(manager.ResolveProviders(new MediaItem(".cbz", null, 10)));
    }

    [Fact]
    public async Task OptInPass_DoesNotSelectTheStandardProviders()
    {
        var archive = EComicFixtures.ComicWithPage();
        var manager = CreateManager(EComicFixtures.Settings(extractCovers: true));

        var optIn = await manager.ProcessAsync(
            new MediaItem(".cbz", null, archive.Length),
            OpenArchive(archive),
            selection: MediaProviderSelection.OptInMetadata);
        Assert.Empty(optIn);

        var standard = await manager.ProcessAsync(
            new MediaItem(".cbz", null, archive.Length),
            OpenArchive(archive),
            selection: MediaProviderSelection.Standard);
        Assert.Equal(2, standard.Count);
        Assert.Contains(standard, result => result.Metadata is not null);
        Assert.Contains(standard, result => result.Preview is not null);
    }

    [Fact]
    public async Task IndexingPass_RunsMetadataAndTheIndexTimeCover()
    {
        var archive = EComicFixtures.ComicWithPage();
        var manager = CreateManager(EComicFixtures.Settings(extractCovers: true));

        Assert.True(manager.HasIndexingPreviewProviders);
        var results = await manager.ProcessAsync(
            new MediaItem(".cbz", null, archive.Length),
            OpenArchive(archive),
            selection: MediaProviderSelection.StandardMetadata);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.False(result.Failed));
        var preview = results.Single(result => result.Preview is not null);
        using var cover = EComicFixtures.Decode(Assert.Single(preview.Preview!.Artifacts).Data);
        Assert.True(Math.Max(cover.Width, cover.Height) <= EComicSettings.DefaultCoverSize);
    }

    [Fact]
    public async Task Scheduler_StoresMetadataAndCoverThatTheCatalogCanPresent()
    {
        var archive = EComicFixtures.ComicWithPage();
        var settings = EComicFixtures.Settings(
            nameSource: EComicNameSource.Series,
            tagWriters: true,
            extractCovers: true,
            coverSize: "256");
        var entry = CreateEntry(archive);
        using var store = new InMemoryMediaCacheStore();
        var scheduler = new MediaExtractionScheduler(CreateManager(settings), store);

        var outcome = await scheduler.ProcessAsync(MediaExtractionRequest.ForCatalogEntry(
            entry,
            OpenArchive(archive),
            new[] { entry }));

        Assert.True(outcome.Published);

        var records = store.GetRecordsForEntry(entry.Id);
        Assert.Equal(2, records.Count);
        var metadata = records.Single(record => record.SchemaId == EComicSchema.MetadataSchemaId);
        Assert.Equal(MediaCacheRecordState.Captured, metadata.State);
        Assert.Equal("Saga",
            metadata.Metadata.Single(value => value.Key == EComicSchema.Series).Text);

        var resolver = new MediaPreviewResolver(store);
        var snapshot = resolver.Resolve(entry);
        Assert.Equal(MediaPreviewState.Ready, snapshot.State);
        Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
        Assert.Equal(PluginId, snapshot.ProviderId);
        Assert.True(resolver.TryReadPayload(snapshot, out var payload));
        using var cover = EComicFixtures.Decode(payload);
        Assert.True(Math.Max(cover.Width, cover.Height) <= 256);

        var displayName = MediaMetadataCatalogEffects.ResolveDisplayName(entry, records);
        Assert.Equal("Saga", displayName);
        entry.SetDisplayName(displayName);
        Assert.Equal("Saga", entry.DisplayName);
        Assert.NotEqual(entry.Name, entry.DisplayName);

        var tagNames = MediaMetadataCatalogEffects.ResolveTagNames(records);
        Assert.Equal(["Brian K. Vaughan", "Fiona Staples"], tagNames);
    }

    [Fact]
    public void SettingsRevision_ChangesWithThePersistedOptions()
    {
        var item = new MediaItem(".cbz", null, 10);
        var seriesManager = CreateManager(
            EComicFixtures.Settings(nameSource: EComicNameSource.Series, extractCovers: true));
        var titleManager = CreateManager(
            EComicFixtures.Settings(nameSource: EComicNameSource.Title, extractCovers: true));

        var series = seriesManager.ResolveProviders(item);
        var title = titleManager.ResolveProviders(item);

        Assert.Equal(2, series.Count);
        Assert.All(series, identity => Assert.NotEmpty(identity.SettingsRevision));
        Assert.NotEqual(series[0].SettingsRevision, title[0].SettingsRevision);
    }

    [Fact]
    public void PluginWithoutSettings_ReportsAnEmptyRevision()
    {
        var manager = new MediaExtensionManager(new[]
        {
            new MediaProvider(PluginId, "1.0.0", new EComicMetadataPlugin())
        });

        var identity = Assert.Single(manager.ResolveProviders(new MediaItem(".cbz", null, 10)));
        Assert.Equal(string.Empty, identity.SettingsRevision);
    }

    [Fact]
    public void AssemblyDiscovery_FindsAllThreeCapabilitiesWithoutAManifest()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(EComicMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("eComic Metadata and Covers", plugin.Name);
        Assert.Equal(3, plugin.Capabilities.Count);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaPreviewCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IPluginConfigurationCapability);
    }

    [Fact]
    public void PackagedPlugin_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = Path.GetDirectoryName(typeof(EComicMetadataPlugin).Assembly.Location)!;

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
    }

    private static MediaExtensionManager CreateManager(EComicSettings settings) =>
        new(new[]
        {
            new MediaProvider(PluginId, "1.0.0", new EComicMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new EComicCoverPlugin())
        }, settingsSource: _ => settings.ToDictionary());

    private static Func<CancellationToken, Task<Stream>> OpenArchive(byte[] archive) =>
        _ => Task.FromResult<Stream>(new MemoryStream(archive, writable: false));

    private static CatalogDocument CreateEntry(byte[] archive) => new()
    {
        Id = "comic-entry-1",
        Name = "saga-001.cbz",
        ParentId = "comics-root",
        Type = CatalogDocumentType.File,
        Size = archive.Length,
        Properties = new PropertiesCollection
        {
            ["FullName"] = @"D:\Comics\saga-001.cbz",
            ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
        }
    };

}
