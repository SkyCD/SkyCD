using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Controls.Preview;
using SkiaSharp;
using Xunit;
using SkyCD.Plugin.Fonts.Export;
using SkyCD.Plugin.Fonts.Models;

namespace SkyCD.Plugin.Fonts.Tests;

/// <summary>
/// End-to-end coverage inside the host pipeline: extension matching, settings-driven behavior, index-time
/// preview storage and discovery without a manifest.
/// </summary>
public sealed class FontHostIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.Fonts";

    [Fact]
    public void Host_ClaimsTheConfiguredFontExtensionsOnly()
    {
        var manager = CreateManager(FontFixtures.Settings(previewsEnabled: true));

        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".ttf", null, 10)).Count);
        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".TTC", null, 10)).Count);
        Assert.Equal(2, manager.ResolveProviders(new MediaItem(".otf", null, 10),
            MediaProviderSelection.StandardMetadata).Count);
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", null, 10)));
    }

    [Fact]
    public void WithoutPreviews_OnlyTheMetadataProviderMatches()
    {
        var manager = CreateManager(FontFixtures.Settings(previewsEnabled: false));

        Assert.Single(manager.ResolveProviders(new MediaItem(".ttf", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".ttf", null, 10),
            MediaProviderSelection.StandardMetadata));
        Assert.Contains(manager.ResolveProviders(new MediaItem(".ttf", null, 10)),
            identity => identity.SchemaId == FontSchema.MetadataSchemaId);
        Assert.DoesNotContain(manager.ResolveProviders(new MediaItem(".ttf", null, 10)),
            identity => identity.SchemaId == FontSchema.PreviewSchemaId);
    }

    [Fact]
    public void DisabledCollection_LeavesNoProvidersForFontFiles()
    {
        var manager = CreateManager(FontFixtures.Settings(collectionEnabled: false, previewsEnabled: true));

        Assert.Empty(manager.ResolveProviders(new MediaItem(".ttf", null, 10)));
    }

    [Fact]
    public async Task IndexingPass_RunsMetadataAndTheIndexTimePreview()
    {
        var font = FontFixtures.Sfnt("Indexed Face", version: "Version 2.0");
        var manager = CreateManager(FontFixtures.Settings(previewsEnabled: true));

        Assert.True(manager.HasIndexingPreviewProviders);
        var results = await manager.ProcessAsync(
            new MediaItem(".ttf", null, font.Length),
            OpenFont(font),
            selection: MediaProviderSelection.StandardMetadata);

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.False(result.Failed));
        var metadata = Assert.Single(results, result => result.Metadata is not null);
        Assert.Contains(metadata.Metadata!.Values, value => value.Key == FontSchema.Version);
        var preview = Assert.Single(results, result => result.Preview is not null);
        Assert.Equal(FontPreviewRenderer.ContentType, Assert.Single(preview.Preview!.Artifacts).ContentType);
    }

    [Fact]
    public async Task Scheduler_StoresMetadataAndPreviewThatTheCatalogCanPresent()
    {
        var font = FontFixtures.Sfnt("Stored Face", copyright: "Copyright 2026 SkyCD");
        var settings = FontFixtures.Settings(previewsEnabled: true, previewSize: "48");
        var entry = CreateEntry(font, "fonts-folder-1");
        using var store = new InMemoryMediaCacheStore();
        var scheduler = new MediaExtractionScheduler(CreateManager(settings), store);

        var outcome = await scheduler.ProcessAsync(MediaExtractionRequest.ForCatalogEntry(
            entry,
            OpenFont(font),
            new[] { entry }));

        Assert.True(outcome.Published);

        var records = store.GetRecordsForEntry(entry.Id);
        Assert.Equal(2, records.Count);
        var metadata = Assert.Single(records, record => record.SchemaId == FontSchema.MetadataSchemaId);
        Assert.Equal(MediaCacheRecordState.Captured, metadata.State);
        Assert.Equal("Copyright 2026 SkyCD",
            Assert.Single(metadata.Metadata, value => value.Key == FontSchema.Copyright).Text);

        var resolver = new MediaPreviewResolver(store);
        var snapshot = resolver.Resolve(entry);
        Assert.Equal(MediaPreviewState.Ready, snapshot.State);
        Assert.Equal(PreviewPayloadKind.Image, snapshot.PayloadKind);
        Assert.Equal(PluginId, snapshot.ProviderId);
        Assert.True(resolver.TryReadPayload(snapshot, out var payload));
        using var image = SKBitmap.Decode(payload);
        Assert.NotNull(image);
    }

    [Fact]
    public void SettingsRevision_ChangesWithThePersistedOptions()
    {
        var item = new MediaItem(".ttf", null, 10);
        var first = CreateManager(FontFixtures.Settings(previewsEnabled: true));
        var second = CreateManager(FontFixtures.Settings(previewsEnabled: true, previewSize: "96"));

        var firstIdentities = first.ResolveProviders(item);
        var secondIdentities = second.ResolveProviders(item);

        Assert.All(firstIdentities, identity => Assert.NotEmpty(identity.SettingsRevision));
        Assert.NotEqual(firstIdentities[0].SettingsRevision, secondIdentities[0].SettingsRevision);
    }

    [Fact]
    public void AssemblyDiscovery_FindsAllThreeCapabilitiesWithoutAManifest()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(FontMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("Font Metadata and Previews", plugin.Name);
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

    private static MediaExtensionManager CreateManager(System.Collections.Generic.Dictionary<string, string?> settings) =>
        new(new[]
        {
            new MediaProvider(PluginId, "1.0.0", new FontMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0",
                new FontPreviewPlugin(FontPluginOptions.Default, _ => SKTypeface.FromFamilyName(null)))
        }, settingsSource: _ => settings);

    private static Func<CancellationToken, Task<Stream>> OpenFont(byte[] font) =>
        _ => Task.FromResult<Stream>(new MemoryStream(font, writable: false));

    private static CatalogDocument CreateEntry(byte[] font, string parentId) => new()
    {
        Id = "font-entry-" + parentId,
        Name = "face.ttf",
        ParentId = parentId,
        Type = CatalogDocumentType.File,
        Size = font.Length,
        Properties = new PropertiesCollection
        {
            ["FullName"] = @"D:\Fonts\face.ttf",
            ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
        }
    };

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Fonts");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the font plugin project directory.");
    }
}
