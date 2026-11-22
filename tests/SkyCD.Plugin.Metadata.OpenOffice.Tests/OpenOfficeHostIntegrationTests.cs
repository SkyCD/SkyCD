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
using SkyCD.Plugin.Metadata.OpenOffice.Models;

namespace SkyCD.Plugin.Metadata.OpenOffice.Tests;

/// <summary>
/// End-to-end coverage inside the host pipeline: extension and content-type matching, provider selection,
/// failure classification, assembly discovery without a manifest, and the metadata the catalog cache stores.
/// </summary>
public sealed class OpenOfficeHostIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.OpenOffice";

    [Fact]
    public void Host_ClaimsOpenOfficeExtensionsAndContentTypesOnly()
    {
        var manager = CreateManager();

        Assert.Single(manager.ResolveProviders(new MediaItem(".odt", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".ODS", null, 10)));
        Assert.Single(manager.ResolveProviders(new MediaItem(".sxw", null, 10)));
        Assert.Single(manager.ResolveProviders(
            new MediaItem(".bin", "application/vnd.oasis.opendocument.spreadsheet", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".txt", "text/plain", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".pdf", "application/pdf", 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".otf", null, 10)));
        Assert.Empty(manager.ResolveProviders(new MediaItem(".otc", null, 10)));
    }

    [Fact]
    public async Task StandardPass_ProducesMetadata()
    {
        var document = OpenOfficeFixtures.OdfDocument();
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".odt", null, document.Length),
            Open(document),
            selection: MediaProviderSelection.Standard);

        var result = Assert.Single(results);
        Assert.False(result.Failed);
        Assert.Equal(PluginId, result.PluginId);
        Assert.NotNull(result.Metadata);
        Assert.Contains(result.Metadata!.Values,
            value => value.Key == OpenOfficeSchema.Title && value.Text == "Annual Report");
        Assert.Contains(result.Metadata.Values,
            value => value.Key == OpenOfficeSchema.Author && value.Text == "A. Writer");
    }

    [Fact]
    public async Task StandardMetadataPass_RunsTheProvider()
    {
        var document = OpenOfficeFixtures.OdfDocument(includeMetadata: false);
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".ott", null, document.Length),
            Open(document),
            selection: MediaProviderSelection.StandardMetadata);

        Assert.Single(results);
        Assert.False(results[0].Failed);
    }

    [Fact]
    public async Task OptInMetadataPass_DoesNotSelectTheProvider()
    {
        var document = OpenOfficeFixtures.OdfDocument();
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".odt", null, document.Length),
            Open(document),
            selection: MediaProviderSelection.OptInMetadata);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ProtectedDocument_IsRecordedAsAFailureRatherThanThrownFromTheHost()
    {
        var document = OpenOfficeFixtures.OdfDocument(
            includeMetadata: false,
            manifestXml: OpenOfficeFixtures.Manifest(true));
        var manager = CreateManager();

        var results = await manager.ProcessAsync(
            new MediaItem(".odt", null, document.Length),
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
        var document = OpenOfficeFixtures.OdfDocument();
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
        Assert.Equal(OpenOfficeSchema.MetadataSchemaId, record.SchemaId);
        Assert.Equal(MediaCacheRecordState.Captured, record.State);
        Assert.Contains(record.Metadata,
            value => value.Key == OpenOfficeSchema.Title && value.Text == "Annual Report");
        Assert.Contains(record.Metadata,
            value => value.Key == OpenOfficeSchema.CreatedAt &&
                     value.Timestamp == new DateTimeOffset(2024, 3, 1, 12, 30, 15, TimeSpan.Zero));
    }

    [Fact]
    public void AssemblyDiscovery_FindsTheMetadataCapabilityWithoutAManifest()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(OpenOfficeMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("OpenOffice Document Metadata", plugin.Name);
        var capability = Assert.Single(plugin.Capabilities);
        Assert.IsType<OpenOfficeMetadataPlugin>(capability);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static MediaExtensionManager CreateManager() =>
        new(new[] { new MediaProvider(PluginId, "1.0.0", new OpenOfficeMetadataPlugin()) });

    private static Func<CancellationToken, Task<Stream>> Open(byte[] document) =>
        _ => Task.FromResult<Stream>(new MemoryStream(document, writable: false));

    private static CatalogDocument CreateEntry(byte[] document) => new()
    {
        Id = "openoffice-entry-1",
        Name = "report.odt",
        ParentId = "documents-root",
        Type = CatalogDocumentType.File,
        Size = document.Length,
        Properties = new PropertiesCollection
        {
            ["FullName"] = @"D:\Documents\report.odt",
            ["LastWriteTime"] = new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero).ToString("O")
        }
    };

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.OpenOffice");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the OpenOffice plugin project directory.");
    }
}
