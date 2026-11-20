using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// End-to-end coverage inside the host pipeline: which sources each capability claims, how the path-addressed
/// OS reader is selected, catalog tag effects and discovery without a manifest.
/// </summary>
public sealed class FileMetadataHostIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.FileMetadata";

    [Fact]
    public void Host_ClaimsEmbeddedCarriersOnceAndOsMetadataForEverySource()
    {
        var manager = CreateManager();

        var tiff = manager.ResolveProviders(new MediaItem(".TIF", null, 10));
        Assert.Equal(
            [FileMetadataSchema.EmbeddedSchemaId, FileMetadataSchema.OsSchemaId],
            tiff.Select(identity => identity.SchemaId));

        var jpeg = manager.ResolveProviders(new MediaItem(".jpg", null, 10));
        Assert.Equal([FileMetadataSchema.OsSchemaId], jpeg.Select(identity => identity.SchemaId));

        Assert.Contains(manager.ResolveProviders(new MediaItem(".txt", null, 10)),
            identity => identity.SchemaId == FileMetadataSchema.OsSchemaId);
    }

    [Fact]
    public async Task IndexingPass_ReturnsEmbeddedMetadataWithoutAPath()
    {
        var tiff = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Utf16(0x9C9E, "sunset; beach", littleEndian: true),
            MetadataFixtures.Short(0x4746, 4, littleEndian: true));
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".tif", null, tiff.Length),
            Open(tiff),
            selection: MediaProviderSelection.StandardMetadata);

        var result = Assert.Single(results);
        Assert.False(result.Failed);
        Assert.Contains(result.Metadata!.Values, value => value.Key == FileMetadataSchema.Tags);
        Assert.Contains(result.Metadata.Values, value => value.Key == FileMetadataSchema.Rating);
    }

    [Fact]
    public async Task OsProvider_RunsWithoutReadingTheStream_WhenAPathIsSupplied()
    {
        var tiff = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Utf16(0x9C9E, "sunset", littleEndian: true));
        var manager = CreateManager();
        var opened = 0;
        Task<Stream> OpenTracked(CancellationToken _)
        {
            Interlocked.Increment(ref opened);
            return Task.FromResult<Stream>(new MemoryStream(tiff, writable: false));
        }

        var results = await manager.ProcessAsync(
            new MediaItem(".tif", null, tiff.Length),
            OpenTracked,
            selection: MediaProviderSelection.StandardMetadata,
            sourcePath: Path.Combine(Path.GetTempPath(), $"skycd-missing-{Guid.NewGuid():N}.tif"));

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.False(result.Failed));
        Assert.Contains(results, result => result.Schema!.Id == FileMetadataSchema.OsSchemaId);
        Assert.Equal(1, opened);
    }

    [Fact]
    public async Task WithoutAPath_TheOsProviderIsSkippedInsteadOfCachedAsAFailure()
    {
        var tiff = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Utf16(0x9C9E, "archive", littleEndian: true));
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".tif", null, tiff.Length),
            Open(tiff),
            selection: MediaProviderSelection.StandardMetadata);

        Assert.DoesNotContain(results, result => result.Failed);
        Assert.DoesNotContain(results, result => result.Schema?.Id == FileMetadataSchema.OsSchemaId);
    }

    [Fact]
    public async Task Scheduler_StoresMetadataThatTheCatalogTurnsIntoTags()
    {
        var tiff = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Utf16(0x9C9E, "sunset; beach", littleEndian: true),
            MetadataFixtures.Short(0x4746, 5, littleEndian: true));
        var entry = CreateEntry(tiff);
        using var store = new InMemoryMediaCacheStore();
        var scheduler = new MediaExtractionScheduler(CreateManager(), store);

        var outcome = await scheduler.ProcessAsync(MediaExtractionRequest.ForCatalogEntry(
            entry,
            Open(tiff),
            [entry]));

        Assert.True(outcome.Published);
        var records = store.GetRecordsForEntry(entry.Id);
        Assert.Contains(records, record =>
            record.State == MediaCacheRecordState.Captured &&
            record.SchemaId == FileMetadataSchema.EmbeddedSchemaId);
        Assert.All(records, record => Assert.NotEqual(MediaCacheRecordState.Failed, record.State));

        var tags = MediaMetadataCatalogEffects.ResolveTagNames(records);
        Assert.Contains("sunset", tags);
        Assert.Contains("beach", tags);
    }

    [Fact]
    public void AssemblyDiscovery_FindsBothCapabilitiesWithoutAManifest()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(FileMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("File Metadata", plugin.Name);
        Assert.Equal(2, plugin.Capabilities.Count);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is ILocalMediaMetadataCapability);
        Assert.DoesNotContain(plugin.Capabilities, capability => capability is IPluginConfigurationCapability);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static MediaExtensionManager CreateManager() =>
        new([
            new MediaProvider(PluginId, "1.0.0", new FileMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new OsMetadataPlugin())
        ]);

    private static Func<CancellationToken, Task<Stream>> Open(byte[] data) =>
        _ => Task.FromResult<Stream>(new MemoryStream(data, writable: false));

    private static CatalogDocument CreateEntry(byte[] data) => new()
    {
        Id = "photo-entry",
        Name = "scan.tif",
        ParentId = "photos-folder",
        Type = CatalogDocumentType.File,
        Size = data.Length,
        Properties = new PropertiesCollection
        {
            ["FullName"] = @"D:\Photos\scan.tif",
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
