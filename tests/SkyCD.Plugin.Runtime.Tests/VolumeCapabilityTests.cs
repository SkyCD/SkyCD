using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class VolumeCapabilityTests
{
    private static readonly MediaTarget Volume = new(MediaTargetKind.Volume, "E:\\", "E:\\");

    private static IReadOnlyList<MediaTargetProviderCapture> Capture(MediaTargetExtensionManager manager) =>
        manager.CaptureAsync(Volume, default).GetAwaiter().GetResult();

    [Fact]
    public void AssemblyDiscoveryHonorsEnabledStateAndExposesMediaTargetCapability()
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
        var manager = new MediaTargetExtensionManager(enabled.SelectMany(p => p.Capabilities
            .OfType<IMediaTargetMetadataCapability>()
            .Select(capability => new MediaTargetProvider(p.Id, p.Version.ToString(), capability))));
        var capture = Assert.Single(Capture(manager));
        Assert.Equal("fake-serial", Assert.Single(capture.Capture.Values).Text);
    }

    [Fact]
    public void FailingProvider_IsIsolatedAndDoesNotBreakCapture()
    {
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("a", "1", new FakeVolumeCapability { Fail = true }),
            new MediaTargetProvider("b", "1", new FakeVolumeCapability())]);

        var capture = Assert.Single(Capture(manager));

        Assert.Equal("fake-serial", Assert.Single(capture.Capture.Values).Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailingDescriptor_IsReportedAndSkippedWithoutLosingHealthyValues(bool throwsCancellation)
    {
        var broken = new FakeVolumeCapability
        {
            DescriptorException = throwsCancellation
                ? new OperationCanceledException()
                : new InvalidOperationException("Private descriptor failure")
        };
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("broken", "1", broken),
            new MediaTargetProvider("a", "1", new FakeVolumeCapability { Text = "low" }),
            new MediaTargetProvider("z", "1", new FakeVolumeCapability { Text = "high", Priority = 100 })]);
        var failures = new ConcurrentBag<PluginFailure>();
        void CaptureFailure(PluginFailure failure) => failures.Add(failure);
        PluginFailure.Reported += CaptureFailure;
        try
        {
            var captures = Capture(manager);
            Assert.Equal(["z", "a"], captures.Select(capture => capture.PluginId));
            Assert.Equal("high", Assert.Single(captures[0].Capture.Values).Text);
            Assert.Equal("low", Assert.Single(captures[1].Capture.Values).Text);
            Assert.False(broken.WasCaptured);
            Assert.Contains(failures, failure => failure.Operation == "media-target");
        }
        finally
        {
            PluginFailure.Reported -= CaptureFailure;
        }
    }

    [Fact]
    public void DescriptorCancellation_WithoutCallerCancellation_IsIsolated()
    {
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("a", "1", new FakeVolumeCapability
            {
                DescriptorException = new OperationCanceledException()
            })]);

        Assert.Empty(Capture(manager));
    }

    [Fact]
    public void ProvidersKeepDeterministicPrecedence()
    {
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("z", "2", new FakeVolumeCapability { Text = "z-value" }),
            new MediaTargetProvider("a", "1", new FakeVolumeCapability { Text = "a-value" })]);

        var captures = Capture(manager);

        Assert.Equal(["a", "z"], captures.Select(capture => capture.PluginId));
    }

    [Fact]
    public void HigherPriorityProviderIsOrderedFirst()
    {
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("a", "1", new FakeVolumeCapability { Text = "low" }),
            new MediaTargetProvider("z", "1", new FakeVolumeCapability { Priority = 100, Text = "high" })]);

        var captures = Capture(manager);

        Assert.Equal(["z", "a"], captures.Select(capture => capture.PluginId));
        Assert.Equal("high", Assert.Single(captures[0].Capture.Values).Text);
    }

    [Fact]
    public async Task CallerCancellation_IsObserved()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var manager = new MediaTargetExtensionManager([new MediaTargetProvider("a", "1", new FakeVolumeCapability
            { DescriptorException = new InvalidOperationException("Must not read after cancellation") })]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.CaptureAsync(Volume, cancellation.Token));
    }

    [Fact]
    public async Task NullTarget_Throws()
    {
        var manager = new MediaTargetExtensionManager([new MediaTargetProvider("a", "1", new FakeVolumeCapability())]);

        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.CaptureAsync(null!, default));
    }

    [Fact]
    public void EmptyProviderList_CapturesNothing()
    {
        var manager = new MediaTargetExtensionManager([]);

        Assert.False(manager.HasProviders);
        Assert.Empty(Capture(manager));
    }
}

public sealed class FakeVolumeCapability : IMediaTargetMetadataCapability
{
    private static readonly MediaMetadataSchema Schema = new("fake", 1,
        [new MediaMetadataField("fakeSerial", MediaMetadataType.Text)]);

    public bool Fail { get; init; }
    public int Priority { get; init; }
    public string Text { get; init; } = "fake-serial";

    public Exception? DescriptorException { get; init; }
    public Action? OnDescriptorRead { get; init; }
    public bool WasCaptured { get; private set; }

    public MediaTargetDescriptor Descriptor
    {
        get
        {
            OnDescriptorRead?.Invoke();
            if (DescriptorException is not null)
            {
                throw DescriptorException;
            }

            return new MediaTargetDescriptor(Priority);
        }
    }

    public bool Supports(MediaTarget target) => target.Kind == MediaTargetKind.Volume;

    public Task<MediaTargetCapture?> CaptureAsync(MediaTarget target, IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        WasCaptured = true;
        if (Fail)
        {
            throw new InvalidOperationException("Private plugin failure");
        }

        return Task.FromResult<MediaTargetCapture?>(new MediaTargetCapture(Schema,
            [new MediaMetadataValue("fakeSerial", MediaMetadataType.Text, Text: Text)]));
    }
}
