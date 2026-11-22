using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Abstractions.Capabilities.Volume;
using SkyCD.Plugin.Host.Volume;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Volume.VolumeSerial.Tests;

public sealed class VolumeSerialDiscoveryTests
{
    [Fact]
    public void AssemblyDiscovery_FindsVolumeCapabilityFromAssemblyMetadata()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(VolumeSerialPlugin).Assembly);

        Assert.Equal("SkyCD.Plugin.VolumeSerial", plugin.Id);
        Assert.Equal("Volume Serial Number", plugin.Name);
        Assert.Equal(new Version(1, 0, 0, 0), plugin.Version);
        Assert.Contains(plugin.Capabilities, capability => capability is VolumeSerialPlugin);
        Assert.Contains(plugin.Capabilities, capability => capability is IVolumeMetadataCapability);
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
        Assert.Contains(Assert.Single(enabled).Capabilities, capability => capability is IVolumeMetadataCapability);
    }

    [Fact]
    public void FileFormatFallback_DoesNotEnableDisabledVolumePlugin()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"skycd-volume-fallback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
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
        Assert.Contains("SkyCD.Plugin.VolumeSerial", manager.GetAvailablePluginIds<IVolumeMetadataCapability>());
        manager.SavePluginEnabledStates(fallbackIds.Select(static id => (id, true)));
        manager.Discover(directory, new Version(3, 0, 0));

        Assert.Empty(manager.Plugins);
        Assert.False(Assert.Single(manager.GetPluginDescriptors()).IsEnabled);
    }

    [Fact]
    public void DiscoveryUsesAssemblyData_NotPluginJson()
    {
        var projectDirectory = FindPluginProjectDirectory();
        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    [Fact]
    public void DisabledPlugin_CapturesNoVolumeSerial()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(VolumeSerialPlugin).Assembly);
        var descriptor = plugin.ToDocument();
        var enabled = new DiscoveredPluginCollection();
        descriptor.IsEnabled = false;
        enabled.Import([plugin], [descriptor]);

        var manager = new VolumeExtensionManager(enabled.SelectMany(p => p.Capabilities.OfType<IVolumeMetadataCapability>()
            .Select(capability => new VolumeProvider(p.Id, p.Version.ToString(), capability))));
        var values = manager.CaptureVolumeMetadata(new VolumeMediaContext("E:\\", "E:\\"), default);

        Assert.Empty(values);
        Assert.False(manager.HasVolumeProviders);
    }

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Volume.VolumeSerial");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the volume serial plugin project directory.");
    }
}
