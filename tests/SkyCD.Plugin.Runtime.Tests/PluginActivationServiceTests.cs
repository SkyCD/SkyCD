using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Activation;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.SDK.Capabilities;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

/// <summary>
/// The enable/disable hook runs off the Options apply path, which re-discovers plugins. These tests pin the
/// contract that keeps that safe: one start per discovery pass, no restart while the same instance stays
/// discovered, and exactly one stop when the capability disappears.
/// </summary>
public sealed class PluginActivationServiceTests
{
    [Fact]
    public async Task RepeatedDiscoveryPasses_ActivateOnceAndKeepTheCapabilityRunning()
    {
        var capability = new CountingCapability();
        var plugins = Plugins(capability);

        var service = new PluginActivationService();
        await service.ApplyAsync(plugins);
        await service.ApplyAsync(plugins);
        await service.ApplyAsync(plugins);

        Assert.Equal(1, capability.Activations);
        Assert.Equal(0, capability.Deactivations);
        Assert.Equal(1, service.ActiveCount);
    }

    [Fact]
    public async Task ReplacedInstance_StopsTheOldCapabilityBeforeStartingTheNewOne()
    {
        var first = new CountingCapability();
        var second = new CountingCapability();
        var service = new PluginActivationService();

        await service.ApplyAsync(Plugins(first));
        await service.ApplyAsync(Plugins(second));

        Assert.Equal(1, first.Activations);
        Assert.Equal(1, first.Deactivations);
        Assert.Equal(1, second.Activations);
        Assert.Equal(0, second.Deactivations);
        Assert.Equal(1, service.ActiveCount);
    }

    [Fact]
    public async Task DisabledPlugin_IsDeactivatedExactlyOnce()
    {
        var capability = new CountingCapability();
        var service = new PluginActivationService();

        await service.ApplyAsync(Plugins(capability));
        await service.ApplyAsync([]);
        await service.ApplyAsync([]);

        Assert.Equal(1, capability.Activations);
        Assert.Equal(1, capability.Deactivations);
        Assert.Equal(0, service.ActiveCount);
    }

    [Fact]
    public async Task ReEnabledPlugin_StartsAgainAfterItsStop()
    {
        var capability = new CountingCapability();
        var service = new PluginActivationService();

        await service.ApplyAsync(Plugins(capability));
        await service.ApplyAsync([]);
        await service.ApplyAsync(Plugins(capability));

        Assert.Equal(2, capability.Activations);
        Assert.Equal(1, capability.Deactivations);
        Assert.Equal(1, service.ActiveCount);
    }

    [Fact]
    public async Task FailingActivation_DoesNotKeepTheCapabilityActive()
    {
        var capability = new CountingCapability { FailOnActivate = true };
        var service = new PluginActivationService();

        await service.ApplyAsync(Plugins(capability));

        Assert.Equal(1, capability.Activations);
        Assert.Equal(0, service.ActiveCount);

        capability.FailOnActivate = false;
        await service.ApplyAsync(Plugins(capability));
        Assert.Equal(2, capability.Activations);
        Assert.Equal(1, service.ActiveCount);
    }

    private static IReadOnlyCollection<DiscoveredPlugin> Plugins(IActivatablePluginCapability capability) =>
    [
        new DiscoveredPlugin
        {
            Id = "SkyCD.Plugin.MotionDetector",
            Name = "Motion detector",
            Version = new Version(1, 0, 0),
            MinHostVersion = new Version(1, 0, 0),
            FileName = "SkyCD.Plugin.Feature.MotionDetector.dll",
            Capabilities = [capability]
        }
    ];

    private sealed class CountingCapability : IActivatablePluginCapability
    {
        public int Activations;

        public int Deactivations;

        public bool FailOnActivate { get; set; }

        public Task ActivateAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Activations);
            if (FailOnActivate)
            {
                throw new InvalidOperationException("Activation failed for the test.");
            }

            return Task.CompletedTask;
        }

        public Task DeactivateAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Deactivations);
            return Task.CompletedTask;
        }
    }
}
