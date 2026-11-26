using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.TestSupport;
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
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(Assert.Single(await first).TimedOut);
            Assert.True(Assert.Single(await manager.ProcessAsync(new MediaItem(".fake", null, 3), OpenTracked)).TimedOut);
            Assert.Equal(1, opens);
            Assert.False(exited.Task.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task LocalMetadataProvider_IsSkippedWithoutAPathInsteadOfFailing()
    {
        var provider = new FakeLocalMetadataCapability();
        var manager = new MediaExtensionManager([new MediaProvider("test", "1", provider)]);

        Assert.True(manager.HasMetadataProviders);
        var results = await manager.ProcessAsync(new MediaItem(".fake", null, 3), Open,
            selection: MediaProviderSelection.StandardMetadata);

        Assert.Empty(results);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task LocalMetadataProvider_ReceivesTheValidatedContext()
    {
        var provider = new FakeLocalMetadataCapability();
        var manager = new MediaExtensionManager([new MediaProvider("test", "1", provider)]);
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("evidence.bin");

        var results = await manager.ProcessAsync(new MediaItem(".fake", "application/fake", 3), Open,
            selection: MediaProviderSelection.StandardMetadata, sourcePath: path);

        var result = Assert.Single(results);
        Assert.False(result.Failed);
        Assert.Equal(path, Assert.Single(result.Metadata!.Values).Text);
        Assert.Equal(path, provider.Context!.Path);
        Assert.Equal(".fake", provider.Context.Extension);
        Assert.Equal("application/fake", provider.Context.ContentType);
        Assert.Equal(3, provider.Context.Length);
    }

    [Fact]
    public async Task LocalMetadataProvider_IsIsolatedWhenItBreaksTheSchema()
    {
        var manager = new MediaExtensionManager(
            [new MediaProvider("test", "1", new FakeLocalMetadataCapability { InvalidKey = true })],
            new MediaExecutionLimits());

        var result = Assert.Single(await manager.ProcessAsync(new MediaItem(".fake", null, 3), Open,
            selection: MediaProviderSelection.StandardMetadata, sourcePath: "somewhere.bin"));

        Assert.True(result.Failed);
    }

    [Fact]
    public void LocalMetadataProvider_IsResolvedForStandardMetadataOnly()
    {
        var manager = new MediaExtensionManager([new MediaProvider("test", "1", new FakeLocalMetadataCapability())]);
        var item = new MediaItem(".fake", null, 3);

        Assert.Single(manager.ResolveProviders(item, MediaProviderSelection.StandardMetadata));
        Assert.Empty(manager.ResolveProviders(item, MediaProviderSelection.OptInMetadata));
    }

    [Fact]
    public async Task LocalPreviewProvider_IsSkippedWithoutAPathInsteadOfFailing()
    {
        var provider = new FakeLocalPreviewCapability();
        var manager = new MediaExtensionManager([new MediaProvider("test", "1", provider)]);

        Assert.True(manager.HasIndexingPreviewProviders);
        var results = await manager.ProcessAsync(new MediaItem(".fake", null, 3), Open,
            selection: MediaProviderSelection.StandardMetadata);

        Assert.Empty(results);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task LocalPreviewProvider_ReceivesTheValidatedContextAndCopiesArtifacts()
    {
        var provider = new FakeLocalPreviewCapability();
        var manager = new MediaExtensionManager([new MediaProvider("test", "1", provider)]);
        using var workspace = new TestWorkspace();
        var path = workspace.GetPath("launcher.desktop");

        var results = await manager.ProcessAsync(new MediaItem(".fake", "application/fake", 3), Open,
            selection: MediaProviderSelection.StandardMetadata, sourcePath: path);

        var result = Assert.Single(results);
        Assert.False(result.Failed);
        provider.Bytes[0] = 99;
        Assert.Equal(1, Assert.Single(result.Preview!.Artifacts).Data.Span[0]);
        Assert.Equal(path, provider.Context!.Path);
        Assert.Equal(".fake", provider.Context.Extension);
        Assert.Equal("application/fake", provider.Context.ContentType);
        Assert.Equal(3, provider.Context.Length);
    }

    [Fact]
    public void LocalPreviewProvider_IsResolvedForPreviewPassesOnly()
    {
        var manager = new MediaExtensionManager([new MediaProvider("test", "1", new FakeLocalPreviewCapability())]);
        var item = new MediaItem(".fake", null, 3);

        Assert.Single(manager.ResolveProviders(item, MediaProviderSelection.Standard));
        Assert.Single(manager.ResolveProviders(item, MediaProviderSelection.StandardMetadata));
        Assert.Empty(manager.ResolveProviders(item, MediaProviderSelection.OptInMetadata));
        Assert.False(manager.HasMetadataProviders);
    }

    [Fact]
    public async Task FormatProbeRejection_SkipsOnlyThatProviderWithoutFailure()
    {
        var rejecting = new ScriptedProbeCapability((_, _, _) => ValueTask.FromResult(false));
        var accepting = new ScriptedProbeCapability((_, _, _) => ValueTask.FromResult(true));
        using var failures = new FailureCapture();
        var manager = new MediaExtensionManager([
            new MediaProvider("a", "1", rejecting),
            new MediaProvider("b", "1", accepting)]);

        var result = Assert.Single(await manager.ProcessAsync(ProbeItem(), OpenProbeSource));

        Assert.Equal("b", result.PluginId);
        Assert.False(result.Failed);
        Assert.Equal(1, rejecting.ProbeCalls);
        Assert.Equal(0, rejecting.ExtractCalls);
        Assert.Equal(1, accepting.ExtractCalls);
        Assert.Empty(failures.Failures);
    }

    [Fact]
    public async Task FormatProbeRejection_DoesNotMaskAnotherProvidersRealFailure()
    {
        var rejecting = new ScriptedProbeCapability((_, _, _) => ValueTask.FromResult(false));
        var failing = new ScriptedProbeCapability(failExtraction: true);
        using var failures = new FailureCapture();
        var manager = new MediaExtensionManager([
            new MediaProvider("a", "1", rejecting),
            new MediaProvider("b", "1", failing)]);

        var result = Assert.Single(await manager.ProcessAsync(ProbeItem(), OpenProbeSource));

        Assert.Equal("b", result.PluginId);
        Assert.True(result.Failed);
        Assert.Equal(0, rejecting.ExtractCalls);
        Assert.Single(failures.Failures);
    }

    [Fact]
    public async Task FormatProbeThrowBudgetOverrunAndTimeout_AreRejectionsNotFailures()
    {
        using var failures = new FailureCapture();
        var throwing = new ScriptedProbeCapability((_, _, _) => throw new IOException("probe broke"));
        var overrunning = new ScriptedProbeCapability(async ValueTask<bool> (_, source, token) =>
        {
            await source.ReadAsync(0, 3, token);
            return true;
        });
        var timingOut = new ScriptedProbeCapability(async ValueTask<bool> (_, _, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return true;
        });
        var limits = new MediaExecutionLimits(ProbeBytes: 2, ProbeTimeoutMilliseconds: 50);

        Assert.Empty(await Create(throwing, limits).ProcessAsync(ProbeItem(), OpenProbeSource));
        Assert.Empty(await Create(overrunning, limits).ProcessAsync(ProbeItem(), OpenProbeSource));
        Assert.Empty(await Create(timingOut, limits).ProcessAsync(ProbeItem(), OpenProbeSource));

        Assert.Equal(0, throwing.ExtractCalls);
        Assert.Equal(0, overrunning.ExtractCalls);
        Assert.Equal(0, timingOut.ExtractCalls);
        Assert.Empty(failures.Failures);
    }

    [Fact]
    public async Task FormatProbe_ThatOutlivesItsBudgetKeepsTheStreamUntilItExits()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capability = new ScriptedProbeCapability(async ValueTask<bool> (_, _, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return true;
        });
        var manager = Create(capability, new MediaExecutionLimits(ProbeTimeoutMilliseconds: 50));

        var results = await manager.ProcessAsync(ProbeItem(), _ => Task.FromResult<Stream>(new DisposalStream(disposed)));
        Assert.Empty(results);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(disposed.Task.IsCompleted);

        release.TrySetResult();
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task FormatProbe_IsNotInvokedByIdentityResolutionOrUnselectedProviders()
    {
        var probe = new ScriptedProbeCapability(optIn: true);
        var manager = Create(probe);
        var item = ProbeItem();

        Assert.Single(manager.ResolveProviders(item));
        Assert.Single(manager.ResolveProviders(item, MediaProviderSelection.OptInOnly));
        Assert.Equal(0, probe.ProbeCalls);

        Assert.Empty(await manager.ProcessAsync(item, OpenProbeSource));
        Assert.Equal(0, probe.ProbeCalls);

        Assert.Single(await manager.ProcessAsync(item, OpenProbeSource, selection: MediaProviderSelection.OptInOnly));
        Assert.Equal(1, probe.ProbeCalls);
    }

    [Fact]
    public async Task FormatProbe_HeaderReplayKeepsTheFullExtractionBudget()
    {
        static async ValueTask<bool> ProbeHeader(MediaItem item, IMediaSource source, CancellationToken token)
        {
            await source.ReadAsync(0, 4, token);
            return true;
        }

        static async Task<MediaExtractionResult> ReadAll(MediaItem item, IMediaSource source, CancellationToken token)
        {
            var head = await source.ReadAsync(0, 4, token);
            var tail = await source.ReadAsync(4, 4, token);
            return new MediaExtractionResult([new("value", MediaMetadataType.Integer, Integer: head.Span[0] * 100 + tail.Span[3])]);
        }

        var limits = new MediaExecutionLimits(SourceBytes: 8, ProbeBytes: 4);
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var seekable = new ScriptedProbeCapability(ProbeHeader, ReadAll);
        var nonSeekable = new ScriptedProbeCapability(ProbeHeader, ReadAll);

        var seekableResult = Assert.Single(await Create(seekable, limits).ProcessAsync(
            new MediaItem(".script", null, 8), _ => Task.FromResult<Stream>(new MemoryStream(data))));
        var nonSeekableResult = Assert.Single(await Create(nonSeekable, limits).ProcessAsync(
            new MediaItem(".script", null, 8), _ => Task.FromResult<Stream>(new NonSeekableStream(data))));

        Assert.False(seekableResult.Failed);
        Assert.Equal(108, Assert.Single(seekableResult.Metadata!.Values).Integer);
        Assert.False(nonSeekableResult.Failed);
        Assert.Equal(108, Assert.Single(nonSeekableResult.Metadata!.Values).Integer);
    }

    [Fact]
    public async Task DiscoveredProbeCapabilities_RouteByHeaderOnASharedExtension()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(Assembly.GetExecutingAssembly());
        var manager = new MediaExtensionManager(plugin.Capabilities
            .Where(capability => capability is ProbeAcceptingMetadataCapability or ProbeRejectingMetadataCapability)
            .Select(capability => new MediaProvider(plugin.Id, plugin.Version.ToString(), capability)));

        var result = Assert.Single(await manager.ProcessAsync(new MediaItem(".probe", null, 8), OpenProbeSource));

        Assert.Equal(typeof(ProbeAcceptingMetadataCapability).FullName, result.Capability);
        Assert.Equal("accept", Assert.Single(result.Metadata!.Values).Text);
    }

    [Fact]
    public async Task FormatProbeAcceptance_StillRunsEverySupportingProvider()
    {
        var first = new ScriptedProbeCapability((_, _, _) => ValueTask.FromResult(true));
        var second = new ScriptedProbeCapability((_, _, _) => ValueTask.FromResult(true));
        var manager = new MediaExtensionManager([
            new MediaProvider("a", "1", first),
            new MediaProvider("b", "1", second)]);

        var results = await manager.ProcessAsync(ProbeItem(), OpenProbeSource);

        Assert.Equal(new[] { "a", "b" }, results.Select(result => result.PluginId));
        Assert.All(results, result => Assert.False(result.Failed));
    }

    [Fact]
    public async Task LocalFormatProbe_IsSkippedWithoutAPathAndRejectsOrAcceptsWithOne()
    {
        using var workspace = new TestWorkspace();
        var accepting = new ProbeLocalMetadataCapability { Accept = true };
        var rejecting = new ProbeLocalMetadataCapability { Accept = false };
        var item = ProbeItem();

        var withoutPath = await Create(accepting).ProcessAsync(item, OpenProbeSource,
            selection: MediaProviderSelection.StandardMetadata);
        Assert.Empty(withoutPath);
        Assert.Equal(0, accepting.ProbeCalls);
        Assert.Equal(0, accepting.ExtractCalls);

        var rejected = await Create(rejecting).ProcessAsync(item, OpenProbeSource,
            selection: MediaProviderSelection.StandardMetadata, sourcePath: workspace.GetPath("shared.probe"));
        Assert.Empty(rejected);
        Assert.Equal(1, rejecting.ProbeCalls);
        Assert.Equal(0, rejecting.ExtractCalls);

        var accepted = await Create(accepting).ProcessAsync(item, OpenProbeSource,
            selection: MediaProviderSelection.StandardMetadata, sourcePath: workspace.GetPath("shared.probe"));
        Assert.Single(accepted);
        Assert.Equal(1, accepting.ProbeCalls);
        Assert.Equal(1, accepting.ExtractCalls);
    }

    private static MediaItem ProbeItem() => new(".script", null, 8);

    private static Task<Stream> OpenProbeSource(CancellationToken token) =>
        Task.FromResult<Stream>(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]));

    private sealed class ScriptedProbeCapability : IMediaMetadataCapability, IMediaFormatProbe
    {
        private readonly Func<MediaItem, IMediaSource, CancellationToken, ValueTask<bool>>? probe;
        private readonly Func<MediaItem, IMediaSource, CancellationToken, Task<MediaExtractionResult>>? extract;

        public ScriptedProbeCapability(
            Func<MediaItem, IMediaSource, CancellationToken, ValueTask<bool>>? probe = null,
            Func<MediaItem, IMediaSource, CancellationToken, Task<MediaExtractionResult>>? extract = null,
            bool failExtraction = false, bool optIn = false)
        {
            this.probe = probe;
            this.extract = extract;
            FailExtraction = failExtraction;
            OptIn = optIn;
        }

        public bool FailExtraction { get; }
        public bool OptIn { get; }
        public int ProbeCalls { get; private set; }
        public int ExtractCalls { get; private set; }

        public MediaDescriptor Descriptor => new([".script"], [],
            new MediaMetadataSchema("script", 1, [new("value", MediaMetadataType.Integer)]), IsOptIn: OptIn);

        public ValueTask<bool> CanHandleAsync(MediaItem item, IMediaSource source, CancellationToken cancellationToken)
        {
            ProbeCalls++;
            return probe is null ? ValueTask.FromResult(true) : probe(item, source, cancellationToken);
        }

        public Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
        {
            ExtractCalls++;
            if (FailExtraction) throw new IOException("Private plugin failure");
            return extract is null
                ? Task.FromResult(new MediaExtractionResult([]))
                : extract(item, source, cancellationToken);
        }
    }

    private sealed class ProbeLocalMetadataCapability : ILocalMediaMetadataCapability, ILocalMediaFormatProbe
    {
        public bool Accept { get; init; }
        public int ProbeCalls { get; private set; }
        public int ExtractCalls { get; private set; }

        public MediaDescriptor Descriptor => new([".script"], [],
            new MediaMetadataSchema("local-probe", 1, [new("evidence", MediaMetadataType.Text)]));

        public ValueTask<bool> CanHandleAsync(LocalMediaContext context, CancellationToken cancellationToken)
        {
            ProbeCalls++;
            return ValueTask.FromResult(Accept);
        }

        public Task<MediaExtractionResult> ExtractAsync(LocalMediaContext source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
        {
            ExtractCalls++;
            return Task.FromResult(new MediaExtractionResult(
                [new MediaMetadataValue("evidence", MediaMetadataType.Text, Text: source.Path)]));
        }
    }

    private sealed class FailureCapture : IDisposable
    {
        private readonly Action<PluginFailure> handler;

        public FailureCapture()
        {
            handler = failure =>
            {
                if (failure.Operation == "media") Failures.Add(failure);
            };
            PluginFailure.Reported += handler;
        }

        public List<PluginFailure> Failures { get; } = [];

        public void Dispose() => PluginFailure.Reported -= handler;
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

    private static MediaExtensionManager Create(SkyCD.SDK.Capabilities.IPluginCapability capability,
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

public sealed class FakeLocalMetadataCapability : ILocalMediaMetadataCapability
{
    public bool InvalidKey { get; init; }
    public int Calls { get; private set; }
    public LocalMediaContext? Context { get; private set; }

    public MediaDescriptor Descriptor => new(["*"], [],
        new MediaMetadataSchema("local-test", 1, [new("evidence", MediaMetadataType.Text)]));

    public Task<MediaExtractionResult> ExtractAsync(LocalMediaContext source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        Calls++;
        Context = source;
        var key = InvalidKey ? "unknown" : "evidence";
        return Task.FromResult(new MediaExtractionResult(
            [new MediaMetadataValue(key, MediaMetadataType.Text, Text: source.Path)]));
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

public sealed class FakeLocalPreviewCapability : ILocalMediaPreviewCapability
{
    public int Calls { get; private set; }
    public LocalMediaContext? Context { get; private set; }
    public byte[] Bytes { get; } = [1, 2, 3];

    public MediaDescriptor Descriptor => new([".fake"], [],
        new MediaMetadataSchema("local-preview", 1, []), IndexTimePreview: true);

    public Task<MediaPreviewResult> GeneratePreviewAsync(LocalMediaContext source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        Calls++;
        Context = source;
        return Task.FromResult(new MediaPreviewResult([new MediaPreviewArtifact("image/png", Bytes)]));
    }
}

public sealed class ProbeAcceptingMetadataCapability : IMediaMetadataCapability, IMediaFormatProbe
{
    public MediaDescriptor Descriptor => new([".probe"], [],
        new MediaMetadataSchema("probe-accept", 1, [new("format", MediaMetadataType.Text)]));

    public async ValueTask<bool> CanHandleAsync(MediaItem item, IMediaSource source,
        CancellationToken cancellationToken)
    {
        await source.ReadAsync(0, 4, cancellationToken);
        return true;
    }

    public Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        Task.FromResult(new MediaExtractionResult(
            [new MediaMetadataValue("format", MediaMetadataType.Text, Text: "accept")]));
}

public sealed class ProbeRejectingMetadataCapability : IMediaMetadataCapability, IMediaFormatProbe
{
    public MediaDescriptor Descriptor => new([".probe"], [],
        new MediaMetadataSchema("probe-reject", 1, [new("format", MediaMetadataType.Text)]));

    public async ValueTask<bool> CanHandleAsync(MediaItem item, IMediaSource source,
        CancellationToken cancellationToken)
    {
        await source.ReadAsync(0, 4, cancellationToken);
        return false;
    }

    public Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("A rejected provider must not be extracted.");
}
