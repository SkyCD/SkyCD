using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Abstractions.Capabilities.Volume;
using SkyCD.Plugin.Host.Volume;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class VolumeCapabilityTests
{
    private static readonly VolumeMediaContext Volume = new("E:\\", "E:\\", "DISC", "CDFS");

    [Fact]
    public void AssemblyDiscoveryHonorsEnabledStateAndExposesVolumeCapability()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(Assembly.GetExecutingAssembly());
        Assert.Contains(plugin.Capabilities, capability => capability is FakeVolumeCapability);
        var descriptor = plugin.ToDocument();

        var enabled = new DiscoveredPluginCollection();
        descriptor.IsEnabled = false;
        enabled.Import([plugin], [descriptor]);
        Assert.Empty(enabled);

        descriptor.IsEnabled = true;
        enabled.Import([plugin], [descriptor]);
        var manager = new VolumeExtensionManager(enabled.SelectMany(p => p.Capabilities.OfType<IVolumeMetadataCapability>()
            .Select(capability => new VolumeProvider(p.Id, p.Version.ToString(), capability))));
        var values = manager.CaptureVolumeMetadata(Volume, default);
        Assert.Equal("fake-serial", Assert.Single(values).Text);
    }

    [Fact]
    public void FailingProvider_IsIsolatedAndDoesNotBreakCapture()
    {
        var manager = new VolumeExtensionManager([
            new VolumeProvider("a", "1", new FakeVolumeCapability { Fail = true }),
            new VolumeProvider("b", "1", new FakeVolumeCapability())]);

        var values = manager.CaptureVolumeMetadata(Volume, default);

        Assert.Equal("fake-serial", Assert.Single(values).Text);
    }

    [Fact]
    public void ProvidersKeepCollidingKeysWithDeterministicPrecedence()
    {
        var manager = new VolumeExtensionManager([
            new VolumeProvider("z", "2", new FakeVolumeCapability { Text = "z-value" }),
            new VolumeProvider("a", "1", new FakeVolumeCapability { Text = "a-value" })]);

        var values = manager.CaptureVolumeMetadata(Volume, default);

        Assert.Equal("a-value", Assert.Single(values).Text);
    }

    [Fact]
    public void HigherPriorityProviderWinsCollisionsBeforePluginId()
    {
        var manager = new VolumeExtensionManager([
            new VolumeProvider("a", "1", new FakeVolumeCapability { Text = "low" }),
            new VolumeProvider("z", "1", new FakeVolumeCapability { Priority = 100, Text = "high" })]);

        Assert.Equal("high", Assert.Single(manager.CaptureVolumeMetadata(Volume, default)).Text);
    }

    [Fact]
    public void CallerCancellation_IsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var manager = new VolumeExtensionManager([new VolumeProvider("a", "1", new FakeVolumeCapability())]);

        Assert.ThrowsAny<OperationCanceledException>(() => manager.CaptureVolumeMetadata(Volume, cancellation.Token));
    }

    [Fact]
    public void NullVolume_Throws()
    {
        var manager = new VolumeExtensionManager([new VolumeProvider("a", "1", new FakeVolumeCapability())]);

        Assert.Throws<ArgumentNullException>(() => manager.CaptureVolumeMetadata(null!, default));
    }

    [Fact]
    public void EmptyProviderList_CapturesNothing()
    {
        var manager = new VolumeExtensionManager([]);

        Assert.False(manager.HasVolumeProviders);
        Assert.Empty(manager.CaptureVolumeMetadata(Volume, default));
    }
}

public sealed class FakeVolumeCapability : IVolumeMetadataCapability
{
    public bool Fail { get; init; }
    public int Priority { get; init; }
    public string Text { get; init; } = "fake-serial";

    public VolumeDescriptor Descriptor => new(new MediaMetadataSchema("fake", 1,
        [new MediaMetadataField("fakeSerial", MediaMetadataType.Text)]), Priority);

    public VolumeExtractionResult Extract(VolumeMediaContext volume, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("Private plugin failure");
        }

        return new VolumeExtractionResult(
            [new MediaMetadataValue("fakeSerial", MediaMetadataType.Text, Text: Text)]);
    }
}
