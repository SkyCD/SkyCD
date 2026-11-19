using System;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Volume;
using SkyCD.Plugin.Host.Volume;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.VolumeSerial.Tests;

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
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.VolumeSerial");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the volume serial plugin project directory.");
    }
}
