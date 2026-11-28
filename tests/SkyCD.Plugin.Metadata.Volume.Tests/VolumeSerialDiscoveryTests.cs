using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Metadata.Volume.Tests;

public sealed class VolumeSerialDiscoveryTests
{
    [Fact]
    public void AssemblyDiscovery_FindsVolumeCapabilityFromAssemblyMetadata()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(VolumeSerialPlugin).Assembly);

        Assert.Equal("SkyCD.Plugin.VolumeSerial", plugin.Id);
        Assert.Equal("Volume Serial Number", plugin.Name);
        Assert.Equal(typeof(VolumeSerialPlugin).Assembly.GetName().Version, plugin.Version);
        Assert.Contains(plugin.Capabilities, capability => capability is VolumeSerialPlugin);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaTargetMetadataCapability);
    }

    [Fact]
    public void EnabledState_ControlsWhetherVolumeCaptureRuns()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(VolumeSerialPlugin).Assembly);
        var descriptor = plugin.ToDocument();
        var enabled = new DiscoveredPluginCollection();

        descriptor.IsEnabled = false;
        enabled.Import([plugin], [descriptor]);
        Assert.Empty(enabled);

        descriptor.IsEnabled = true;
        enabled.Import([plugin], [descriptor]);
        Assert.Single(enabled);
        Assert.Contains(Assert.Single(enabled).Capabilities, capability => capability is IMediaTargetMetadataCapability);
    }

    [Fact]
    public void FileFormatFallback_DoesNotEnableDisabledVolumePlugin()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory($"skycd-volume-fallback-{Guid.NewGuid():N}");
        var assemblyPath = typeof(VolumeSerialPlugin).Assembly.Location;
        File.Copy(assemblyPath, Path.Combine(directory, Path.GetFileName(assemblyPath)));
        using var databaseManager = new DatabaseManager();
        databaseManager.Connect("default", Path.Combine(directory, "database"));
        var repositoryManager = new RepositoryManager(databaseManager);
        var repository = (IRepository<PluginDocument>)repositoryManager.For<PluginDocument>();
        var manager = new PluginManager(NullLogger<PluginManager>.Instance,
            new AssembliesListFactory(NullLogger<AssembliesListFactory>.Instance),
            new DiscoveredPluginFactory(), repository);
        manager.Discover(directory, new Version(3, 0, 0));
        manager.SavePluginEnabledStates([("SkyCD.Plugin.VolumeSerial", false)]);
        manager.Discover(directory, new Version(3, 0, 0));

        var fallbackIds = manager.GetAvailablePluginIds<IFileFormatPluginCapability>();
        Assert.Empty(fallbackIds);
        Assert.Contains("SkyCD.Plugin.VolumeSerial", manager.GetAvailablePluginIds<IMediaTargetMetadataCapability>());
        manager.SavePluginEnabledStates(fallbackIds.Select(static id => (id, true)));
        manager.Discover(directory, new Version(3, 0, 0));

        Assert.Empty(manager.Plugins);
        Assert.False(Assert.Single(manager.GetPluginDescriptors()).IsEnabled);
    }

    [Fact]
    public void DiscoveryUsesAssemblyData_NotPluginJson()
    {
        var projectDirectory = Path.GetDirectoryName(typeof(VolumeSerialPlugin).Assembly.Location)!;
        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
    }

    [Fact]
    public async Task DisabledPlugin_CapturesNoVolumeSerial()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(VolumeSerialPlugin).Assembly);
        var descriptor = plugin.ToDocument();
        var enabled = new DiscoveredPluginCollection();
        descriptor.IsEnabled = false;
        enabled.Import([plugin], [descriptor]);

        var manager = new MediaTargetExtensionManager(enabled.SelectMany(p => p.Capabilities
            .OfType<IMediaTargetMetadataCapability>()
            .Select(capability => new MediaTargetProvider(p.Id, p.Version.ToString(), capability))));
        var captures = await manager.CaptureAsync(new MediaTarget(MediaTargetKind.Volume, "E:\\", "E:\\"), default);

        Assert.Empty(captures);
        Assert.False(manager.HasProviders);
    }

}
