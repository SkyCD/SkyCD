using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Legacy.Scd;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CatalogLoadServiceTests
{
    [Fact]
    public async Task LoadAsync_SynchronousPluginDoesNotBlockCallerAndProgressIsBounded()
    {
        var path = Path.GetTempFileName();
        using var release = new ManualResetEventSlim();
        var reader = new BlockingReader(release);
        var progress = new RecordingProgress();
        try
        {
            var service = new CatalogLoadService(new FileFormatManager([reader]));
            var task = service.LoadAsync(path, (payload, importing) =>
            {
                importing.Report(20);
                importing.Report(10);
                importing.Report(90);
                return payload;
            }, progress);
            await reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(task.IsCompleted);
            Assert.Contains(0, progress.Values);
            release.Set();
            Assert.Equal("catalog", await task.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(progress.Values.Order(), progress.Values);
            Assert.Equal(100, progress.Values.Last());
            Assert.InRange(progress.Values.Count, 1, 102);
            Assert.False(reader.Source!.CanRead);
        }
        finally
        {
            release.Set();
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("binary-sample.scd")]
    [InlineData("text-sample.scd")]
    public async Task LoadAsync_ImportsLegacyCatalogAndBuildsPersistedTree(string file)
    {
        var service = new CatalogLoadService(new FileFormatManager([new LegacyScdPlugin()]));
        var progress = new RecordingProgress();
        var count = await service.LoadAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", file), (payload, importing) =>
        {
            var documents = CatalogImportTests.Extract(payload!);
            importing.Report(20);
            CatalogImportTests.Persist(documents);
            importing.Report(90);
            return documents.Count;
        }, progress);
        Assert.Equal(5, count);
        Assert.Equal(progress.Values.Order(), progress.Values);
        Assert.Equal(100, progress.Values.Last());
    }

    [Fact]
    public async Task FailedLoad_PropagatesErrorAndProgressStateCanBeCleared()
    {
        var vm = MainWindowViewModel.CreateForInMemoryCatalog([]);
        vm.IsStatusBarVisible = false;
        vm.BeginCatalogLoad();
        Assert.True(vm.IsStatusBarShown);
        Assert.True(vm.IsProgressVisible);
        Assert.False(vm.IsCatalogInteractionEnabled);
        vm.ReportCatalogLoadProgress(75);
        vm.ReportCatalogLoadProgress(20);
        Assert.Equal(75, vm.ProgressValue);
        try
        {
            var service = new CatalogLoadService(new FileFormatManager([new LegacyScdPlugin()]));
            await Assert.ThrowsAsync<FileNotFoundException>(() => service.LoadAsync(
                Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".scd"), (_, _) => 0, new RecordingProgress()));
            vm.StatusText = "Load failed";
        }
        finally
        {
            vm.EndCatalogLoad();
        }
        vm.ReportCatalogLoadProgress(100);
        Assert.Equal("Load failed", vm.StatusText);
        Assert.False(vm.IsProgressVisible);
        Assert.False(vm.IsStatusBarShown);
        Assert.False(vm.IsStatusBarVisible);
        Assert.True(vm.IsCatalogInteractionEnabled);
        Assert.Equal(0, vm.ProgressValue);
    }

    private sealed class RecordingProgress : IProgress<int>
    {
        public ConcurrentQueue<int> Values { get; } = new();
        public void Report(int value) => Values.Enqueue(value);
    }

    private sealed class BlockingReader(ManualResetEventSlim release) : IFileFormatPluginCapability
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Stream? Source { get; private set; }
        public FileFormatDescriptor SupportedFormat => new("blocking", "Blocking reader", [".tmp"], [], true, false);

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
        {
            Source = request.Source;
            Entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10), cancellationToken)) throw new TimeoutException();
            for (var i = 0; i <= 1000; i++) request.Progress!.Report(i % 101);
            return Task.FromResult(new FileFormatReadResult { Success = true, Payload = "catalog" });
        }

        public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new FileFormatWriteResult { Success = false });
    }
}
