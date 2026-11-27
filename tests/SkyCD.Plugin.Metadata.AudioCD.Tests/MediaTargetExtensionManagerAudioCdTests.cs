using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Metadata.AudioCD.Models;
using Xunit;

namespace SkyCD.Plugin.Metadata.AudioCD.Tests;

/// <summary>
/// Optical-disc side of the shared <see cref="MediaTargetExtensionManager"/>: the disc flows take the first
/// capture, so priority ordering, support filtering, settings delivery and fault isolation must hold for a
/// disc target the same way they do for a volume.
/// </summary>
public sealed class MediaTargetExtensionManagerAudioCdTests
{
    private static readonly MediaTarget Disc = new(MediaTargetKind.OpticalDisc, "E:\\");

    private static readonly MediaMetadataSchema FakeSchema = new("fake.disc", 1,
        [new MediaMetadataField("Album", MediaMetadataType.Text)]);

    private static IReadOnlyList<MediaTargetProviderCapture> Capture(MediaTargetExtensionManager manager) =>
        manager.CaptureAsync(Disc, CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public void HighestPriorityProvider_IsFirst()
    {
        var low = new FakeCapability(priority: 1, "low");
        var high = new FakeCapability(priority: 10, "high");
        var manager = new MediaTargetExtensionManager(
        [
            new MediaTargetProvider("low.plugin", "1.0", low),
            new MediaTargetProvider("high.plugin", "1.0", high)
        ]);

        var captures = Capture(manager);

        var first = captures.First();
        Assert.Equal("high.plugin", first.PluginId);
        Assert.Equal("high", first.Capture.Values.Single().Text);
        Assert.Equal(MediaTargetKind.OpticalDisc, high.LastTarget!.Kind);
        Assert.Equal("E:\\", high.LastTarget.Location);
    }

    [Fact]
    public void PersistedSettings_ArePassedToTheCapability()
    {
        var capability = new FakeCapability(priority: 0, "disc");
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [AudioCdSettings.OfflineKey] = "true"
        };
        var manager = new MediaTargetExtensionManager(
            [new MediaTargetProvider("skycd.audio-cd", "1.0", capability)],
            pluginId => pluginId == "skycd.audio-cd"
                ? settings
                : new Dictionary<string, string?>());

        Capture(manager);

        Assert.Same(settings, capability.LastTarget!.Settings);
    }

    [Fact]
    public void FailingProvider_IsIsolatedAndTheNextOneRuns()
    {
        var broken = new FakeCapability(priority: 10, "broken") { Throw = true };
        var working = new FakeCapability(priority: 1, "working");
        var manager = new MediaTargetExtensionManager(
        [
            new MediaTargetProvider("broken.plugin", "1.0", broken),
            new MediaTargetProvider("working.plugin", "1.0", working)
        ]);

        var first = Capture(manager).First();

        Assert.Equal("working.plugin", first.PluginId);
        Assert.True(working.WasCaptured);
    }

    [Fact]
    public void VolumeOnlyProvider_IsSkippedForADisc()
    {
        var volumeOnly = new FakeCapability(priority: 10, "volume") { Supported = false };
        var manager = new MediaTargetExtensionManager([new MediaTargetProvider("volume.plugin", "1.0", volumeOnly)]);

        Assert.Empty(Capture(manager));
        Assert.False(volumeOnly.WasCaptured);
    }

    private sealed class FakeCapability : IMediaTargetMetadataCapability
    {
        private readonly string text;

        public FakeCapability(int priority = 0, string text = "value")
        {
            Priority = priority;
            this.text = text;
        }

        public int Priority { get; }

        public bool Throw { get; init; }

        public bool Supported { get; init; } = true;

        public string? PrimaryExtension => "cda";

        public bool WasCaptured { get; private set; }

        public MediaTarget? LastTarget { get; private set; }

        public MediaTargetDescriptor Descriptor => new(Priority, PrimaryExtension);

        public bool Supports(MediaTarget target) => Supported;

        public Task<MediaTargetCapture?> CaptureAsync(MediaTarget target, IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken)
        {
            WasCaptured = true;
            LastTarget = target;
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw)
            {
                throw new InvalidOperationException("provider exploded");
            }

            var capture = new MediaTargetCapture(
                FakeSchema,
                [new MediaMetadataValue("Album", MediaMetadataType.Text, Text: text)],
                new MediaMetadataSchema("fake.track", 1, [new MediaMetadataField("Title", MediaMetadataType.Text)]),
                []);
            return Task.FromResult<MediaTargetCapture?>(capture);
        }
    }
}
