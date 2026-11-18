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
        var service = new MediaHashService(CreateManager(new HashMetadataPlugin()), store);
        var entry = CreateEntry(path);
        var content = TestMediaSource.ReadFile(path);
        var expectedSha = HashReference.Sha256(content);
        var expectedEd2k = HashReference.Ed2k(content);

        var outcome = await service.ComputeAsync(entry, [entry]);

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
        var service = new MediaHashService(CreateManager(new HashMetadataPlugin()), store);
        var entry = CreateEntry(path);
        Assert.True((await service.ComputeAsync(entry, [entry])).Stored);
        var captured = store.GetRecordsForEntry(entry.Id)
            .ToDictionary(record => record.Id, record => record.Metadata.ToArray());

        WriteFile("changed.bin", 4000);
        var outcome = await service.ComputeAsync(entry, [entry]);

        Assert.False(outcome.Stored);
        Assert.Equal(MediaHashSkipReason.SourceChanged, outcome.SkipReason);
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
        var service = new MediaHashService(CreateManager(capability), store);
        var entry = CreateEntry(path);

        var failed = await service.ComputeAsync(entry, [entry]);
        Assert.False(failed.Stored);
        Assert.Equal(MediaHashSkipReason.Failed, failed.SkipReason);
        var resolver = new MediaPreviewResolver(store);
        var failedSnapshot = resolver.Resolve(entry);
        Assert.Equal(MediaPreviewState.Failed, failedSnapshot.State);
        Assert.True(Assert.Single(failedSnapshot.Fields).IsFailed);

        capability.Fail = false;
        Assert.True((await service.ComputeAsync(entry, [entry])).Stored);
        Assert.Equal("ok", Assert.Single(resolver.Resolve(entry).Fields).Value);

        capability.Fail = true;
        var refreshFailure = await service.ComputeAsync(entry, [entry]);
        Assert.False(refreshFailure.Stored);
        Assert.Equal("ok", Assert.Single(resolver.Resolve(entry).Fields).Value);
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

    private static CatalogDocument CreateEntry(string path)
    {
        var file = new FileInfo(path);
        return new CatalogDocument
        {
            Id = Guid.NewGuid().ToString("N"),
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
