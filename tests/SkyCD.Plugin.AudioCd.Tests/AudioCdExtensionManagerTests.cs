using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.AudioCd;
using Xunit;

namespace SkyCD.Plugin.AudioCd.Tests;

public sealed class AudioCdExtensionManagerTests
{
    [Fact]
    public void NoProviders_ReportsUnavailable() =>
        Assert.False(new AudioCdExtensionManager([]).HasProviders);

    [Fact]
    public async Task HighestPriorityProviderWins()
    {
        var low = new FakeCapability(priority: 1, "low");
        var high = new FakeCapability(priority: 10, "high");
        var manager = new AudioCdExtensionManager(
        [
            new AudioCdProvider("low.plugin", "1.0", low),
            new AudioCdProvider("high.plugin", "1.0", high)
        ]);

        var capture = await manager.CaptureAsync("E:\\", CancellationToken.None);

        Assert.NotNull(capture);
        Assert.Equal("high.plugin", capture.PluginId);
        Assert.Equal("high", capture.Capture.DiscValues.Single().Text);
        Assert.Equal(0, low.Calls);
    }

    [Fact]
    public async Task PersistedSettings_ArePassedToTheProvider()
    {
        var capability = new FakeCapability(priority: 0, "disc");
        IReadOnlyDictionary<string, string?>? seen = null;
        capability.OnCapture = request => seen = request.Settings;
        var manager = new AudioCdExtensionManager(
            [new AudioCdProvider("skycd.audio-cd", "1.0", capability)],
            pluginId => pluginId == "skycd.audio-cd"
                ? new Dictionary<string, string?> { ["offline"] = "true" }
                : new Dictionary<string, string?>());

        await manager.CaptureAsync("E:\\", CancellationToken.None);

        Assert.NotNull(seen);
        Assert.Equal("true", seen["offline"]);
    }

    [Fact]
    public async Task FailingProvider_IsIsolatedAndTheNextOneRuns()
    {
        var broken = new FakeCapability(priority: 10, "broken")
        {
            OnCapture = _ => throw new InvalidOperationException("provider exploded")
        };
        var working = new FakeCapability(priority: 1, "working");
        var manager = new AudioCdExtensionManager(
        [
            new AudioCdProvider("broken.plugin", "1.0", broken),
            new AudioCdProvider("working.plugin", "1.0", working)
        ]);

        var capture = await manager.CaptureAsync("E:\\", CancellationToken.None);

        Assert.NotNull(capture);
        Assert.Equal("working.plugin", capture.PluginId);
        Assert.Equal(1, working.Calls);
    }

    [Fact]
    public async Task BrokenSettingsResolver_FallsBackToPluginDefaults()
    {
        var capability = new FakeCapability(priority: 0, "disc");
        IReadOnlyDictionary<string, string?>? seen = null;
        capability.OnCapture = request => seen = request.Settings;
        var manager = new AudioCdExtensionManager(
            [new AudioCdProvider("skycd.audio-cd", "1.0", capability)],
            _ => throw new InvalidOperationException("configuration store offline"));

        var capture = await manager.CaptureAsync("E:\\", CancellationToken.None);

        Assert.NotNull(capture);
        Assert.NotNull(seen);
        Assert.Empty(seen);
    }

    [Fact]
    public async Task BlankRoot_NeverCallsAProvider()
    {
        var capability = new FakeCapability(priority: 0, "disc");
        var manager = new AudioCdExtensionManager([new AudioCdProvider("plugin", "1.0", capability)]);

        Assert.Null(await manager.CaptureAsync(" ", CancellationToken.None));
        Assert.Equal(0, capability.Calls);
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var manager = new AudioCdExtensionManager([new AudioCdProvider("plugin", "1.0", new FakeCapability())]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            manager.CaptureAsync("E:\\", cancelled.Token));
    }

    private sealed class FakeCapability : IAudioCdMetadataCapability
    {
        private readonly string text;

        public FakeCapability(int priority = 0, string text = "value")
        {
            Priority = priority;
            this.text = text;
        }

        public int Priority { get; }

        public string? PrimaryExtension => "cda";

        public int Calls { get; private set; }

        public Action<AudioCdDiscRequest>? OnCapture { get; set; }

        public Task<AudioCdCapture?> CaptureAsync(AudioCdDiscRequest request, IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            OnCapture?.Invoke(request);

            var capture = new AudioCdCapture(
                new MediaMetadataSchema("test.disc", 1, [new MediaMetadataField("Album", MediaMetadataType.Text)]),
                [new MediaMetadataValue("Album", MediaMetadataType.Text, Text: text)],
                new MediaMetadataSchema("test.track", 1, [new MediaMetadataField("Title", MediaMetadataType.Text)]),
                []);
            return Task.FromResult<AudioCdCapture?>(capture);
        }
    }
}
