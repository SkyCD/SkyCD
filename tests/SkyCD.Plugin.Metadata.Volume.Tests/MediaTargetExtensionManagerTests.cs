using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using Xunit;
using SkyCD.Plugin.Metadata.Volume.Models;

namespace SkyCD.Plugin.Metadata.Volume.Tests;

/// <summary>
/// Host-level <see cref="MediaTargetExtensionManager"/> behavior and schema edge cases not covered by the
/// per-plugin tests: support filtering, provider ordering, settings resolution, fault isolation and input
/// rejection. Value merging for volumes happens in the scanner, which consumes these captures.
/// </summary>
public sealed class MediaTargetExtensionManagerTests
{
    private static readonly MediaTarget Volume = new(MediaTargetKind.Volume, @"C:\", "C:\\");

    private static readonly MediaMetadataSchema FakeSchema = new("fake", 1, []);

    private static MediaMetadataValue MakeValue(string key, string text) =>
        new(key, MediaMetadataType.Text, Text: text);

    private sealed class FakeCapability : IMediaTargetMetadataCapability
    {
        public IReadOnlyList<MediaMetadataValue> Values { get; init; } = [];
        public bool Throw { get; init; }
        public bool Supported { get; init; } = true;
        public int Priority { get; init; }
        public bool WasCaptured { get; private set; }
        public MediaTarget? LastTarget { get; private set; }

        public MediaTargetDescriptor Descriptor => new(Priority);

        public bool Supports(MediaTarget target) => Supported;

        public Task<MediaTargetCapture?> CaptureAsync(MediaTarget target, IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken)
        {
            WasCaptured = true;
            LastTarget = target;
            if (Throw) throw new InvalidOperationException("Fake plugin failure");
            return Task.FromResult<MediaTargetCapture?>(Values.Count == 0
                ? null
                : new MediaTargetCapture(FakeSchema, Values));
        }
    }

    private static IReadOnlyList<MediaTargetProviderCapture> Capture(MediaTargetExtensionManager manager) =>
        manager.CaptureAsync(Volume, CancellationToken.None).GetAwaiter().GetResult();

    [Fact]
    public void NoProviders_ReturnsEmpty()
    {
        var manager = new MediaTargetExtensionManager([]);

        Assert.Empty(Capture(manager));
        Assert.False(manager.HasProviders);
    }

    [Fact]
    public void SingleProvider_ReturnsItsCapture()
    {
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("a", "1", new FakeCapability { Values = [MakeValue("k1", "v1")] })]);

        var capture = Assert.Single(Capture(manager));

        Assert.Equal("a", capture.PluginId);
        Assert.Single(capture.Capture.Values, v => v.Key == "k1" && v.Text == "v1");
        Assert.True(manager.HasProviders);
    }

    [Fact]
    public void MultipleProviders_CollectAllCaptures()
    {
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("a", "1", new FakeCapability { Values = [MakeValue("k1", "v1")] }),
            new MediaTargetProvider("b", "1", new FakeCapability { Values = [MakeValue("k2", "v2")] }),
        ]);

        var captures = Capture(manager);

        Assert.Equal(2, captures.Count);
        Assert.Equal(["a", "b"], captures.Select(capture => capture.PluginId));
    }

    [Fact]
    public void ProvidersAreOrderedByPriorityThenPluginId()
    {
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("zzz", "1", new FakeCapability { Values = [MakeValue("k", "z")] }),
            new MediaTargetProvider("bbb", "1",
                new FakeCapability { Priority = 100, Values = [MakeValue("k2", "high")] }),
            new MediaTargetProvider("aaa", "1", new FakeCapability { Values = [MakeValue("k3", "a")] }),
        ]);

        var captures = Capture(manager);

        Assert.Equal(["bbb", "aaa", "zzz"], captures.Select(capture => capture.PluginId));
    }

    [Fact]
    public void UnsupportedProvider_IsSkipped()
    {
        var unsupported = new FakeCapability { Supported = false, Values = [MakeValue("k", "v")] };
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("a", "1", unsupported),
            new MediaTargetProvider("b", "1", new FakeCapability { Values = [MakeValue("ok", "yes")] }),
        ]);

        var capture = Assert.Single(Capture(manager));

        Assert.Equal("b", capture.PluginId);
        Assert.False(unsupported.WasCaptured);
    }

    [Fact]
    public void FaultyProvider_DoesNotBreakRemainingProviders()
    {
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("bad", "1", new FakeCapability { Throw = true }),
            new MediaTargetProvider("good", "1", new FakeCapability { Values = [MakeValue("ok", "yes")] }),
        ]);

        var capture = Assert.Single(Capture(manager));

        Assert.Contains(capture.Capture.Values, v => v.Key == "ok");
    }

    [Fact]
    public async Task CancelledToken_ThrowsBeforeAnyCapture()
    {
        var capability = new FakeCapability();
        var manager = new MediaTargetExtensionManager([new MediaTargetProvider("a", "1", capability)]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.CaptureAsync(Volume, cts.Token));
        Assert.False(capability.WasCaptured);
    }

    [Fact]
    public async Task NullTarget_Throws()
    {
        var manager = new MediaTargetExtensionManager([]);

        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.CaptureAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void NullCapture_IsNotAdded()
    {
        var manager = new MediaTargetExtensionManager([
            new MediaTargetProvider("a", "1", new FakeCapability { Values = [] })]);

        Assert.Empty(Capture(manager));
    }

    [Fact]
    public void Settings_AreResolvedPerProvider()
    {
        var capability = new FakeCapability { Values = [MakeValue("k", "v")] };
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["option"] = "value" };
        var manager = new MediaTargetExtensionManager(
            [new MediaTargetProvider("skycd.test", "1", capability)],
            pluginId => string.Equals(pluginId, "skycd.test", StringComparison.Ordinal)
                ? settings
                : new Dictionary<string, string?>());

        Capture(manager);

        Assert.Same(settings, capability.LastTarget?.Settings);
    }

    [Fact]
    public void SettingsFailure_FallsBackToDefaults()
    {
        var capability = new FakeCapability { Values = [MakeValue("k", "v")] };
        var manager = new MediaTargetExtensionManager(
            [new MediaTargetProvider("a", "1", capability)],
            _ => throw new InvalidOperationException("Configuration store failure"));

        var capture = Assert.Single(Capture(manager));

        Assert.Empty(capability.LastTarget?.Settings ?? new Dictionary<string, string?>());
        Assert.Single(capture.Capture.Values);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")]
    [InlineData("1234-5678-9ABC")]
    [InlineData("XYZW-1234")]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("1234_5678")]
    [InlineData("1234 5678")]
    public void TryParse_RejectsInvalidInput(string? text)
    {
        Assert.False(VolumeSerialSchema.TryParse(text, out _));
    }

    [Fact]
    public void TryParse_AcceptsLowerCaseHex()
    {
        Assert.True(VolumeSerialSchema.TryParse("abcd-1234", out var serial));
        Assert.Equal(0xABCD1234u, serial);
    }
}
