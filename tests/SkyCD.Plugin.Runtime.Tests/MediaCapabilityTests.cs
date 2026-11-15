using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

public sealed class MediaCapabilityTests
{
    [Fact]
    public async Task AssemblyDiscoveryHonorsEnabledStateAndPreservesExistingCapabilities()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(Assembly.GetExecutingAssembly());
        Assert.Contains(plugin.Capabilities, c => c is FakeMediaCapability);
        Assert.Contains(plugin.Capabilities, c => c is not IMediaMetadataCapability);
        var descriptor = plugin.ToDocument();
        var enabled = new DiscoveredPluginCollection();
        descriptor.IsEnabled = false;
        enabled.Import([plugin], [descriptor]);
        Assert.Empty(enabled);
        descriptor.IsEnabled = true;
        enabled.Import([plugin], [descriptor]);
        var manager = new MediaExtensionManager(enabled.SelectMany(p => p.Capabilities.Select(c =>
            new MediaProvider(p.Id, p.Version.ToString(), c))));
        var results = await manager.ProcessAsync(new MediaItem(".fake", null, 3), Open);
        var result = Assert.Single(results, r => r.Metadata is not null);
        Assert.Equal(plugin.Id, result.PluginId);
        Assert.Equal(plugin.Version.ToString(), result.PluginVersion);
        Assert.Equal(3, Assert.Single(result.Metadata!.Values).Integer);
    }

    [Fact]
    public async Task SourceBudgetFailureIsIsolatedAndStreamDisposed()
    {
        var stream = new MemoryStream([1, 2, 3]);
        var manager = Create(new FakeMediaCapability(), new MediaExecutionLimits(SourceBytes: 2));
        var result = Assert.Single(await manager.ProcessAsync(new MediaItem(".fake", null, 3), _ => Task.FromResult<Stream>(stream)));
        Assert.True(result.Failed);
        Assert.False(stream.CanRead);
    }

    [Fact]
    public async Task PreviewArtifactsAreBoundedAndCopied()
    {
        var plugin = new FakePreviewCapability();
        var manager = Create(plugin);
        var result = Assert.Single(await manager.ProcessAsync(new MediaItem(".fake", null, 3), Open));
        plugin.Bytes[0] = 99;
        Assert.Equal(1, result.Preview!.Artifacts[0].Data.Span[0]);
        manager = Create(plugin, new MediaExecutionLimits(ArtifactBytes: 1));
        Assert.True(Assert.Single(await manager.ProcessAsync(new MediaItem(".fake", null, 3), Open)).Failed);
    }

    [Fact]
    public async Task ProvidersKeepCollidingKeysWithDeterministicPrecedence()
    {
        var manager = new MediaExtensionManager([
            new MediaProvider("z", "2", new FakeMediaCapability()),
            new MediaProvider("a", "1", new FakeMediaCapability())]);
        var results = await manager.ProcessAsync(new MediaItem(".fake", null, 3), Open);
        Assert.Equal(new[] { "a", "z" }, results.Select(r => r.PluginId));
        Assert.All(results, r => Assert.Equal("length", Assert.Single(r.Metadata!.Values).Key));
    }

    [Fact]
    public async Task ErrorsDoNotPreventOtherProviders()
    {
        var manager = new MediaExtensionManager([
            new MediaProvider("a", "1", new FakeMediaCapability { Fail = true }),
            new MediaProvider("b", "1", new FakeMediaCapability())]);
        var results = await manager.ProcessAsync(new MediaItem(".fake", null, 3), Open);
        Assert.Single(results, r => r.Failed);
        Assert.Single(results, r => !r.Failed);
    }

    [Fact]
    public async Task TimeoutAndCallerCancellationAreDistinct()
    {
        var manager = Create(new FakeMediaCapability { Delay = true }, new MediaExecutionLimits(TimeoutMilliseconds: 50));
        Assert.True(Assert.Single(await manager.ProcessAsync(new MediaItem(".fake", null, 3), Open)).TimedOut);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ProcessAsync(
            new MediaItem(".fake", null, 3), Open, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task UncooperativeWorkerRetainsSlotAndDisposesStreamOnExit()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = Create(new BlockedMediaCapability(entered, release),
            new MediaExecutionLimits(Concurrency: 1, TimeoutMilliseconds: 200));
        var opens = 0;
        Task<Stream> OpenTracked(CancellationToken token)
        {
            Interlocked.Increment(ref opens);
            return Task.FromResult<Stream>(new DisposalStream(exited));
        }
        try
        {
            var first = manager.ProcessAsync(new MediaItem(".fake", null, 3), OpenTracked);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(Assert.Single(await first).TimedOut);
            Assert.True(Assert.Single(await manager.ProcessAsync(new MediaItem(".fake", null, 3), OpenTracked)).TimedOut);
            Assert.Equal(1, opens);
            Assert.False(exited.Task.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class BlockedMediaCapability(TaskCompletionSource entered, TaskCompletionSource release) : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor => new([".fake"], [], new MediaMetadataSchema("blocked", 1, []));
        public async Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release.Task;
            return new MediaExtractionResult([]);
        }
    }

    private sealed class DisposalStream(TaskCompletionSource disposed) : MemoryStream
    {
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            disposed.TrySetResult();
        }
    }

    private static MediaExtensionManager Create(SkyCD.Plugin.Abstractions.Capabilities.IPluginCapability capability,
        MediaExecutionLimits? limits = null) => new([new MediaProvider("test", "1", capability)], limits);

    private static Task<Stream> Open(CancellationToken token) => Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));
}

public sealed class FakeMediaCapability : IMediaMetadataCapability
{
    public bool Delay { get; init; }
    public bool Fail { get; init; }
    public MediaDescriptor Descriptor => new([".fake"], ["application/fake"],
        new MediaMetadataSchema("test", 1, [new("length", MediaMetadataType.Integer, "byte")]));

    public async Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        if (Fail) throw new IOException("Private plugin failure");
        if (Delay) await Task.Delay(Timeout.Infinite, cancellationToken);
        var bytes = await source.ReadAsync(0, 3, cancellationToken);
        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult([new("length", MediaMetadataType.Integer, Integer: bytes.Length)]);
    }
}

public sealed class FakePreviewCapability : IMediaPreviewCapability
{
    public byte[] Bytes { get; } = [1, 2, 3];
    public MediaDescriptor Descriptor => new([".fake"], [], new MediaMetadataSchema("preview", 1, []));
    public Task<MediaPreviewResult> GeneratePreviewAsync(MediaItem item, IMediaSource source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        Task.FromResult(new MediaPreviewResult([new("image/png", Bytes)]));
}
