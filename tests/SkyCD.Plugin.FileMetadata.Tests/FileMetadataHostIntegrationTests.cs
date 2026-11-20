using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
using Xunit;

namespace SkyCD.Plugin.FileMetadata.Tests;

/// <summary>
/// End-to-end coverage inside the host pipeline: extension matching, settings-driven behavior, catalog tag
/// effects and discovery without a manifest.
/// </summary>
public sealed class FileMetadataHostIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.FileMetadata";

    [Fact]
    public void Host_ClaimsTheConfiguredExtensionsOnly()
    {
        var manager = CreateManager(FileMetadataSettings.Default);

        Assert.Single(manager.ResolveProviders(new MediaItem(".jpg", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".XMP", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".tif", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", null, 10)));
    }

    [Fact]
    public void DisabledCollection_LeavesNoProvidersForAnySource()
    {
        var manager = CreateManager(FileMetadataSettings.Default, collectionEnabled: false);

        Assert.Empty(manager.ResolveProviders(new MediaItem(".jpg", null, 10)));
    }

    [Fact]
    public async Task IndexingPass_ReturnsMetadataFromTheExtractor()
    {
        var photo = MetadataFixtures.Jpeg(MetadataFixtures.Xmp(
            keywords: ["sunset"],
            hierarchical: ["Places|Beach"],
            description: "Evening",
            rating: 4));
        var manager = CreateManager(FileMetadataSettings.Default);

        var results = await manager.ProcessAsync(
            new MediaItem(".jpg", null, photo.Length),
            Open(photo),
            selection: MediaProviderSelection.StandardMetadata);

        var result = Assert.Single(results);
        Assert.False(result.Failed);
        Assert.Contains(result.Metadata!.Values, value => value.Key == FileMetadataSchema.Keywords);
        Assert.Contains(result.Metadata.Values, value => value.Key == FileMetadataSchema.Rating);
    }

    [Fact]
    public async Task Scheduler_StoresMetadataThatTheCatalogTurnsIntoTags()
    {
        var photo = MetadataFixtures.Jpeg(MetadataFixtures.Xmp(
            keywords: ["sunset", "beach"],
            hierarchical: ["Places|Beach|Sunset"],
            rating: 5));
        var entry = CreateEntry(photo);
        using var store = new InMemoryMediaCacheStore();
        var scheduler = new MediaExtractionScheduler(CreateManager(FileMetadataSettings.Default), store);

        var outcome = await scheduler.ProcessAsync(MediaExtractionRequest.ForCatalogEntry(
            entry,
            Open(photo),
            new[] { entry }));

        Assert.True(outcome.Published);
        var record = Assert.Single(store.GetRecordsForEntry(entry.Id));
        Assert.Equal(MediaCacheRecordState.Captured, record.State);
        Assert.Equal(FileMetadataSchema.SchemaId, record.SchemaId);

        var tags = MediaMetadataCatalogEffects.ResolveTagNames(store.GetRecordsForEntry(entry.Id));
        Assert.Contains("sunset", tags);
        Assert.Contains("beach", tags);
        Assert.Contains("Places", tags);
        Assert.Contains("Places / Beach", tags);
        Assert.Contains("Places / Beach / Sunset", tags);
    }

    [Fact]
    public void SettingsRevision_ChangesWithThePersistedOptions()
    {
        var item = new MediaItem(".jpg", null, 10);
        var first = CreateManager(FileMetadataSettings.Default).ResolveProviders(item);
        var second = CreateManager(new FileMetadataSettings(true, true, splitHierarchicalTags: false, true, true,
            [".jpg"])).ResolveProviders(item);

        Assert.All(first, identity => Assert.NotEmpty(identity.SettingsRevision));
        Assert.NotEqual(first[0].SettingsRevision, second[0].SettingsRevision);
    }

    [Fact]
    public void AssemblyDiscovery_FindsBothCapabilitiesWithoutAManifest()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(FileMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("File Metadata", plugin.Name);
        Assert.Equal(2, plugin.Capabilities.Count);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IPluginConfigurationCapability);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static MediaExtensionManager CreateManager(FileMetadataSettings settings, bool? collectionEnabled = null)
    {
        var dictionary = new Dictionary<string, string?>(settings.ToDictionary())
        {
            [FileMetadataSettings.CollectionEnabledKey] =
                (collectionEnabled ?? settings.CollectionEnabled) ? "true" : "false"
        };
        return new MediaExtensionManager(
            [new MediaProvider(PluginId, "1.0.0", new FileMetadataPlugin())],
            settingsSource: _ => dictionary);
    }

    private static Func<CancellationToken, Task<Stream>> Open(byte[] data) =>
        _ => Task.FromResult<Stream>(new MemoryStream(data, writable: false));

    private static CatalogDocument CreateEntry(byte[] data) => new()
    {
        Id = "photo-entry",
        Name = "photo.jpg",
        ParentId = "photos-folder",
        Type = CatalogDocumentType.File,
        Size = data.Length,
        Properties = new PropertiesCollection
        {
            ["FullName"] = @"D:\Photos\photo.jpg",
            ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
        }
    };

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.FileMetadata");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the file metadata plugin project directory.");
    }
}
