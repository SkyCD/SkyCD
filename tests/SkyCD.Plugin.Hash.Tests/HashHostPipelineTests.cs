using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Media;
using SkyCD.Presentation.MediaCache;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.Staging;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Preview;
using Xunit;

namespace SkyCD.Plugin.Hash.Tests;

public sealed class HashHostPipelineTests : IDisposable
{
    private readonly string directory =
        Path.Combine(AppContext.BaseDirectory, "hash-test-data", Guid.NewGuid().ToString("N"));

    public HashHostPipelineTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task OptInProvider_IsSkippedByImplicitPass_AndRunsOnlyWhenSelected()
    {
        var path = WriteFile("sample.bin", 4096);
        var manager = CreateManager(new HashMetadataPlugin());
        var item = new MediaItem(".bin", null, new FileInfo(path).Length);

        var implicitResults = await manager.ProcessAsync(item, token => OpenReadAsync(path, token));

        Assert.Empty(implicitResults);

        var explicitResults = await manager.ProcessAsync(
            item,
            token => OpenReadAsync(path, token),
            limits: new MediaExecutionLimits(SourceBytes: 4096, Concurrency: 1, TimeoutMilliseconds: 30_000),
            selection: MediaProviderSelection.OptInOnly);

        var result = Assert.Single(explicitResults);
        Assert.False(result.Failed);
        Assert.Equal(3, result.Metadata!.Values.Count);
        Assert.All(result.Metadata.Values, value => Assert.False(string.IsNullOrWhiteSpace(value.Text)));
    }

    [Fact]
    public async Task Compute_StoresHashes_ThatStayReadableAfterTheSourceIsGone()
    {
        var path = WriteFile("offline.bin", 5000);
        var store = MediaCacheStore.Create();
        var service = new MediaMetadataService(CreateManager(new HashMetadataPlugin()), store);
        var entry = CreateEntry(path);
        var content = TestMediaSource.ReadFile(path);
        var expectedSha = HashReference.Sha256(content);
        var expectedEd2k = HashReference.Ed2k(content);

        var outcome = await service.CollectAsync(entry, [entry]);

        Assert.True(outcome.Stored);
        var records = store.GetRecordsForEntry(entry.Id);
        var record = Assert.Single(records);
        Assert.Equal(MediaCacheRecordState.Captured, record.State);
        Assert.Equal(3, record.Metadata.Count);

        File.Delete(path);

        var snapshot = new MediaPreviewResolver(store).Resolve(entry);
        var values = snapshot.Fields.ToDictionary(field => field.Key, field => field.Value?.ToString());
        Assert.Equal(expectedSha, values[HashSchema.Sha256]);
        Assert.Equal(expectedEd2k, values[HashSchema.Ed2k]);
        Assert.Equal(MediaPreviewState.Ready, snapshot.State);
    }

    [Fact]
    public async Task Compute_WhenSourceChanged_DoesNotReplaceStoredHashes()
    {
        var path = WriteFile("changed.bin", 3000);
        var store = MediaCacheStore.Create();
        var service = new MediaMetadataService(CreateManager(new HashMetadataPlugin()), store);
        var entry = CreateEntry(path);
        Assert.True((await service.CollectAsync(entry, [entry])).Stored);
        var captured = store.GetRecordsForEntry(entry.Id)
            .ToDictionary(record => record.Id, record => record.Metadata.ToArray());

        WriteFile("changed.bin", 4000);
        var outcome = await service.CollectAsync(entry, [entry]);

        Assert.False(outcome.Stored);
        Assert.Equal(MediaMetadataSkipReason.SourceChanged, outcome.SkipReason);
        var after = store.GetRecordsForEntry(entry.Id).ToDictionary(record => record.Id, record => record.Metadata.ToArray());
        Assert.Equal(captured.Count, after.Count);
        foreach (var (id, metadata) in captured)
        {
            Assert.Equal(metadata.Select(value => value.Text), after[id].Select(value => value.Text));
        }
    }

    [Fact]
    public async Task FailedCapture_IsShownAsFailed_AndLaterFailuresKeepCapturedValues()
    {
        var path = WriteFile("flaky.bin", 512);
        var store = MediaCacheStore.Create();
        var capability = new FlakyCapability();
        var service = new MediaMetadataService(CreateManager(capability), store);
        var entry = CreateEntry(path);

        var failed = await service.CollectAsync(entry, [entry]);
        Assert.False(failed.Stored);
        Assert.Equal(MediaMetadataSkipReason.Failed, failed.SkipReason);
        var resolver = new MediaPreviewResolver(store);
        var failedSnapshot = resolver.Resolve(entry);
        Assert.Equal(MediaPreviewState.Failed, failedSnapshot.State);
        Assert.True(Assert.Single(failedSnapshot.Fields).IsFailed);

        capability.Fail = false;
        Assert.True((await service.CollectAsync(entry, [entry])).Stored);
        Assert.Equal("ok", Assert.Single(resolver.Resolve(entry).Fields).Value);

        capability.Fail = true;
        Assert.True((await service.CollectAsync(entry, [entry])).Stored);
        var refresh = MediaExtractionRequest.ForCatalogEntry(entry, token => OpenReadAsync(path, token), [entry],
            forceRefresh: true, providerSelection: MediaProviderSelection.OptInOnly);
        var refreshFailure = await new MediaExtractionScheduler(CreateManager(capability), store).ProcessAsync(refresh);
        Assert.True(refreshFailure.SourceUnavailable);
        Assert.Equal("ok", Assert.Single(resolver.Resolve(entry).Fields).Value);
    }

    [Fact]
    public async Task AddingScans_CollectsMetadataBeforePublication_AndSerializesOverlappingAdds()
    {
        WriteFile("first.bin", 4096);
        var scanned = new FolderCatalogScanner().Scan(new FolderScanOptions(directory));
        var first = Assert.Single(scanned, entry => entry.Type == SkyCD.Documents.Enum.CatalogDocumentType.File);
        var second = CreateEntry(WriteFile("second.bin", 2048));
        var hash = new CountingHashCapability(block: true);
        var standard = new StandardCapability();
        var preview = new PreviewCapability();
        using var store = MediaCacheStore.Create();
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog([], mediaCacheStore: store);
        viewModel.RefreshMediaServices(new MediaExtensionManager([
            new MediaProvider("hash", "1", hash),
            new MediaProvider("standard", "1", standard),
            new MediaProvider("preview", "1", preview)]));
        using var staging = new InMemoryScanStagingStore();
        var session = staging.CreateSession();
        staging.StageDocuments(session.SessionId, scanned);

        var adding = viewModel.PublishStagedScanAsync(staging, session.SessionId);
        await hash.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(adding.IsCompleted);
        Assert.DoesNotContain(viewModel.TreeNodes, node => node.Title == scanned[0].Name);
        Assert.Empty(store.GetRecordsForEntry(first.Id));
        var secondAdd = viewModel.AddImportedDocumentsAsync([second]);
        Assert.False(secondAdd.IsCompleted);
        hash.Release();
        Assert.NotNull(await adding.WaitAsync(TimeSpan.FromSeconds(10)));
        await secondAdd.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, hash.Calls);
        Assert.Equal(2, standard.Calls);
        Assert.Equal(0, standard.PreviewCalls);
        Assert.Equal(0, preview.Calls);
        foreach (var entry in new[] { first, second })
        {
            var records = store.GetRecordsForEntry(entry.Id);
            Assert.Equal(2, records.Count);
            var record = Assert.Single(records, record => record.PluginId == "hash");
            Assert.Equal(MediaCacheRecordState.Captured, record.State);
            Assert.Equal(new[] { "ed2k", "sha256", "tth" },
                record.Metadata.Select(value => value.Key).OrderBy(key => key, StringComparer.Ordinal));
            Assert.All(record.Metadata, value => Assert.False(string.IsNullOrWhiteSpace(value.Text)));
        }
    }

    [Fact]
    public async Task CancellationDuringMetadataCollection_DoesNotPublishCatalogOrCache()
    {
        var entry = CreateEntry(WriteFile("cancel.bin", 128), "library");
        var hash = new CountingHashCapability(block: true);
        using var store = MediaCacheStore.Create();
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog([], mediaCacheStore: store);
        viewModel.RefreshMediaServices(new MediaExtensionManager([
            new MediaProvider("standard", "1", new StandardCapability()),
            new MediaProvider("hash", "1", hash)]));
        using var cancellation = new CancellationTokenSource();

        var adding = viewModel.AddImportedDocumentsAsync([entry], cancellation.Token);
        await hash.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adding);

        Assert.DoesNotContain(viewModel.BrowserItems, item => item.Id == entry.Id);
        Assert.Empty(store.GetAllRecords());
        Assert.True(Assert.Single(store.GetBatches()).IsDiscarded);
    }

    [Fact]
    public async Task NoOptInProvider_CollectsStandardMetadataWithoutHashingOrMenuItems()
    {
        var first = CreateEntry(WriteFile("unavailable.bin", 128));
        var second = CreateEntry(WriteFile("disabled.bin", 128));
        var capability = new CountingHashCapability();
        using var store = MediaCacheStore.Create();
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog([], mediaCacheStore: store);
        var statuses = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainWindowViewModel.StatusText)) statuses.Add(viewModel.StatusText);
        };
        viewModel.RefreshMediaServices(new MediaExtensionManager([new MediaProvider("standard", "1", new StandardCapability())]));

        await viewModel.AddImportedDocumentsAsync([first]);
        viewModel.RefreshMediaServices(CreateManager(capability));
        viewModel.RefreshMediaServices(new MediaExtensionManager([]));
        await viewModel.AddImportedDocumentsAsync([second]);

        Assert.False(viewModel.IsProgressVisible);
        Assert.Equal(0, capability.Calls);
        Assert.Equal("standard", Assert.Single(store.GetAllRecords()).PluginId);
        Assert.DoesNotContain(statuses, status => status.Contains("hash", StringComparison.OrdinalIgnoreCase));
        foreach (var item in viewModel.ToolsMenuItems.Concat(viewModel.BrowserContextMenuItems))
        {
            Assert.NotEqual("compute_hashes", item.Key);
            Assert.DoesNotContain("hash", item.Header ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task FailedMetadataProvider_DoesNotPreventHashOrCatalogPublication()
    {
        var entry = CreateEntry(WriteFile("failure.bin", 128), "library");
        var hash = new CountingHashCapability();
        using var store = MediaCacheStore.Create();
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog([], mediaCacheStore: store);
        viewModel.RefreshMediaServices(new MediaExtensionManager([
            new MediaProvider("standard", "1", new FailingStandardCapability()),
            new MediaProvider("hash", "1", hash)]));

        await viewModel.AddImportedDocumentsAsync([entry]);

        Assert.Equal(1, hash.Calls);
        Assert.Contains(viewModel.BrowserItems, item => item.Id == entry.Id);
        var records = store.GetRecordsForEntry(entry.Id);
        Assert.Equal(MediaCacheRecordState.Failed, Assert.Single(records, record => record.PluginId == "standard").State);
        Assert.Equal(MediaCacheRecordState.Captured, Assert.Single(records, record => record.PluginId == "hash").State);
    }

    [Fact]
    public async Task CachedStandardMetadata_DoesNotHideFailedOptInProvider()
    {
        var path = WriteFile("cached-standard.bin", 128);
        var entry = CreateEntry(path);
        var manager = new MediaExtensionManager([
            new MediaProvider("standard", "1", new StandardCapability()),
            new MediaProvider("hash", "1", new FlakyCapability())]);
        using var store = MediaCacheStore.Create();
        var request = MediaExtractionRequest.ForCatalogEntry(entry, token => OpenReadAsync(path, token), [entry]);
        await new MediaExtractionScheduler(manager, store).ProcessAsync(request);

        var outcome = await new MediaMetadataService(manager, store).CollectAsync(entry, [entry]);

        Assert.Equal(MediaMetadataSkipReason.Failed, outcome.SkipReason);
        var records = store.GetRecordsForEntry(entry.Id);
        Assert.Equal(MediaCacheRecordState.Captured, Assert.Single(records, record => record.PluginId == "standard").State);
        Assert.Equal(MediaCacheRecordState.Failed, Assert.Single(records, record => record.PluginId == "hash").State);
    }

    [Fact]
    public async Task RemoteEntries_DoNotReadMatchingLocalPaths()
    {
        var root = new CatalogDocument
        {
            Id = "remote",
            Name = "remote",
            Type = SkyCD.Documents.Enum.CatalogDocumentType.NetworkFolder
        };
        var entry = CreateEntry(WriteFile("local.bin", 128), root.Id);
        var hash = new CountingHashCapability();
        using var store = MediaCacheStore.Create();
        using var viewModel = MainWindowViewModel.CreateForInMemoryCatalog([], mediaCacheStore: store);
        viewModel.RefreshMediaServices(CreateManager(hash));

        await viewModel.AddImportedDocumentsAsync([root, entry]);

        Assert.Equal(0, hash.Calls);
        Assert.Empty(store.GetAllRecords());
    }

    [Fact]
    public async Task CachedHashes_AreReusedWithOtherProviders_WithoutRereading()
    {
        var path = WriteFile("cached.bin", 4096);
        var entry = CreateEntry(path);
        var hash = new CountingHashCapability();
        var standard = new StandardCapability();
        var manager = new MediaExtensionManager([
            new MediaProvider("skycd.hash", "1", hash),
            new MediaProvider("standard", "1", standard)]);
        using var store = MediaCacheStore.Create();
        var scheduler = new MediaExtractionScheduler(manager, store);
        var request = MediaExtractionRequest.ForCatalogEntry(entry, token => OpenReadAsync(path, token), [entry]);

        await scheduler.ProcessAsync(request);
        var service = new MediaMetadataService(manager, store);
        Assert.True(service.CanCollect);
        Assert.True((await service.CollectAsync(entry, [entry])).Stored);
        Assert.True((await service.CollectAsync(entry, [entry])).Stored);
        Assert.True((await scheduler.ProcessAsync(request)).Reused);

        Assert.Equal(1, hash.Calls);
        Assert.Equal(1, standard.Calls);
        Assert.Equal(2, store.GetRecordsForEntry(entry.Id).Count);
        Assert.All(store.GetRecordsForEntry(entry.Id), record => Assert.Equal(MediaCacheRecordState.Captured, record.State));
    }

    private sealed class CountingHashCapability(bool block = false) : IMediaMetadataCapability
    {
        private readonly HashMetadataPlugin inner = new();
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls => Volatile.Read(ref calls);
        public MediaDescriptor Descriptor => inner.Descriptor;
        public void Release() => gate.TrySetResult();

        public async Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            Started.TrySetResult();
            if (block) await gate.Task.WaitAsync(cancellationToken);
            return await inner.ExtractAsync(item, source, progress, cancellationToken);
        }
    }

    private sealed class StandardCapability : IMediaMetadataCapability, IMediaPreviewCapability
    {
        public int Calls { get; private set; }
        public int PreviewCalls { get; private set; }
        public MediaDescriptor Descriptor { get; } = new(["*"], ["*"],
            new MediaMetadataSchema("skycd.test.standard", 1,
                [new MediaMetadataField("standard", MediaMetadataType.Text)]), Priority: 0);

        public Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new MediaExtractionResult(
                [new MediaMetadataValue("standard", MediaMetadataType.Text, Text: "ok")]));
        }

        public Task<MediaPreviewResult> GeneratePreviewAsync(MediaItem item, IMediaSource source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
        {
            PreviewCalls++;
            return Task.FromResult(new MediaPreviewResult([]));
        }
    }

    private sealed class FailingStandardCapability : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor { get; } = new(["*"], ["*"],
            new MediaMetadataSchema("skycd.test.failure", 1,
                [new MediaMetadataField("failure", MediaMetadataType.Text)]), Priority: 0);

        public Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated metadata failure.");
    }

    private sealed class PreviewCapability : IMediaPreviewCapability
    {
        public int Calls { get; private set; }
        public MediaDescriptor Descriptor { get; } = new(["*"], ["*"],
            new MediaMetadataSchema("skycd.test.preview", 1, []), Priority: 0);

        public Task<MediaPreviewResult> GeneratePreviewAsync(MediaItem item, IMediaSource source,
            IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new MediaPreviewResult([]));
        }
    }

    private MediaExtensionManager CreateManager(IMediaMetadataCapability capability) =>
        new([new MediaProvider("skycd.hash", "1.0.0", capability)]);

    private string WriteFile(string name, int length)
    {
        var path = Path.Combine(directory, name);
        var data = new byte[length];
        for (var index = 0; index < data.Length; index++)
        {
            data[index] = (byte)(index % 251);
        }

        File.WriteAllBytes(path, data);
        return path;
    }

    private static CatalogDocument CreateEntry(string path, string? parentId = null)
    {
        var file = new FileInfo(path);
        return new CatalogDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            ParentId = parentId,
            Name = file.Name,
            Type = SkyCD.Documents.Enum.CatalogDocumentType.File,
            Size = file.Length,
            Properties = new SkyCD.Documents.Collections.PropertiesCollection
            {
                ["FullName"] = path,
                ["LastWriteTime"] = file.LastWriteTimeUtc.ToString("O")
            }
        };
    }

    private static Task<Stream> OpenReadAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult(stream);
    }

    private sealed class FlakyCapability : IMediaMetadataCapability
    {
        public bool Fail { get; set; } = true;

        public MediaDescriptor Descriptor { get; } = new(
            ["*"],
            ["*"],
            new MediaMetadataSchema("skycd.test.flaky", 1,
                [new MediaMetadataField("flaky", MediaMetadataType.Text)]),
            Priority: 0,
            IsOptIn: true);

        public async Task<MediaExtractionResult> ExtractAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken)
        {
            await source.ReadAsync(0, (int)Math.Min(item.Length, 1), cancellationToken);
            if (Fail)
            {
                throw new InvalidOperationException("Simulated extraction failure.");
            }

            return new MediaExtractionResult([new MediaMetadataValue("flaky", MediaMetadataType.Text, Text: "ok")]);
        }
    }
}
