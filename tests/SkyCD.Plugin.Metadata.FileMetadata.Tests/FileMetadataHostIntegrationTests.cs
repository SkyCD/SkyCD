using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.TestSupport;
using Xunit;
using SkyCD.Plugin.Metadata.FileMetadata.Models;

namespace SkyCD.Plugin.Metadata.FileMetadata.Tests;

/// <summary>
/// End-to-end coverage inside the host pipeline: which sources the OS capability claims, how the path-addressed
/// reader is selected and skipped, and discovery without a manifest. Embedded formats are covered by the
/// plugins that claim them.
/// </summary>
public sealed class FileMetadataHostIntegrationTests
{
    private const string PluginId = "SkyCD.Plugin.FileMetadata";

    [Fact]
    public void Host_ClaimsOsMetadataForEverySource()
    {
        var manager = CreateManager();

        foreach (var item in new[]
                 {
                     new MediaItem(".tif", null, 10), new MediaItem(".jpg", null, 10),
                     new MediaItem(".txt", null, 10), new MediaItem(string.Empty, null, 10)
                 })
        {
            Assert.Equal([FileMetadataSchema.OsSchemaId],
                manager.ResolveProviders(item).Select(identity => identity.SchemaId));
        }
    }

    [Fact]
    public async Task WithoutAPath_TheOsProviderIsSkippedAndNothingIsRead()
    {
        var manager = CreateManager();
        var opened = 0;
        Task<Stream> OpenTracked(CancellationToken _)
        {
            Interlocked.Increment(ref opened);
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3], writable: false));
        }

        var results = await manager.ProcessAsync(
            new MediaItem(".tif", null, 3),
            OpenTracked,
            selection: MediaProviderSelection.StandardMetadata);

        Assert.Empty(results);
        Assert.Equal(0, opened);
    }

    [Fact]
    public async Task WithAPath_TheProviderRunsWithoutReadingTheStream()
    {
        using var workspace = new TestWorkspace();
        var manager = CreateManager();
        var opened = 0;
        Task<Stream> OpenTracked(CancellationToken _)
        {
            Interlocked.Increment(ref opened);
            return Task.FromResult<Stream>(new MemoryStream([1, 2, 3], writable: false));
        }

        var results = await manager.ProcessAsync(
            new MediaItem(".txt", null, 3),
            OpenTracked,
            selection: MediaProviderSelection.StandardMetadata,
            sourcePath: workspace.GetPath("missing.txt"));

        var result = Assert.Single(results);
        Assert.False(result.Failed);
        Assert.Equal(FileMetadataSchema.OsSchemaId, result.Schema!.Id);
        Assert.Equal(0, opened);
    }

    [Fact]
    public void AssemblyDiscovery_FindsTheOsCapabilityWithoutAManifest()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(OsMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal("File Metadata", plugin.Name);
        var capability = Assert.Single(plugin.Capabilities);
        Assert.IsAssignableFrom<ILocalMediaMetadataCapability>(capability);
        Assert.DoesNotContain(plugin.Capabilities, found => found is IPluginConfigurationCapability);
    }

    private static MediaExtensionManager CreateManager() =>
        new([new MediaProvider(PluginId, "1.0.0", new OsMetadataPlugin())]);
}
