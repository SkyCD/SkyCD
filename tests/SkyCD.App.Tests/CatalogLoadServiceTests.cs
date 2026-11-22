using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.LegacySkyCD;
using SkyCD.Plugin.FileFormat.Json;
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
    public async Task NewCatalog_ClearsTheWorkingCopy_AndSavesOnlyNewEntries()
    {
        var originalPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "text-sample.scd");
        var originalBytes = await File.ReadAllBytesAsync(originalPath);
        var formats = new FileFormatManager([new LegacyScdPlugin(), new JsonCatalogPlugin()]);
        var loader = new CatalogLoadService(formats);
        var original = await loader.LoadAsync(originalPath,
            (payload, _) => CatalogImportTests.Extract(payload!), new RecordingProgress());

        var databaseDirectory = Path.Combine(Path.GetTempPath(), $"skycd-new-catalog-{Guid.NewGuid():N}");
        var databaseManager = new DatabaseManager();
        global::Couchbase.Lite.Database? database = null;
        try
        {
            database = databaseManager.Connect("default", databaseDirectory);
            var repositories = new RepositoryManager(databaseManager);
            var repository = Assert.IsType<CatalogDocumentRepository>(repositories.For<CatalogDocument>());
            foreach (var entry in original)
            {
                repository.Save(entry.Id, entry);
            }

            using var newCatalog = MainWindowViewModel.CreateNewCatalog(repositories);

            Assert.Empty(repository.GetAll());
            Assert.Empty(newCatalog.TreeNodes);
            Assert.Empty(newCatalog.BrowserItems);
            Assert.Empty(newCatalog.GetAllCatalogEntries());
            Assert.Null(newCatalog.SelectedTreeNode);
            Assert.Null(newCatalog.SelectedBrowserItem);
            Assert.Null(newCatalog.CurrentCatalogPath);
            Assert.False(newCatalog.IsDirtyDocument);

            var newPath = Path.Combine(Path.GetTempPath(), $"skycd-new-catalog-{Guid.NewGuid():N}.json");
            try
            {
                var added = newCatalog.AddTitleOnlyMedia("New disk", parentId: null);
                await new CatalogSaveService(formats).SaveAsync(newPath, newCatalog.GetAllCatalogEntries());
                newCatalog.CompleteSaveCatalog(newPath);
                var saved = await loader.LoadAsync(newPath, (payload, _) => CatalogImportTests.Extract(payload!),
                    new RecordingProgress());
                Assert.Equal(added.Id, Assert.Single(saved).Id);
                Assert.False(newCatalog.IsDirtyDocument);
            }
            finally
            {
                File.Delete(newPath);
            }

            var reopened = await loader.LoadAsync(originalPath,
                (payload, _) => CatalogImportTests.Extract(payload!), new RecordingProgress());
            Assert.Equal(original.Select(entry => entry.Id), reopened.Select(entry => entry.Id));
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(originalPath));
        }
        finally
        {
            database?.Dispose();
            databaseManager.Dispose();
            if (Directory.Exists(databaseDirectory)) Directory.Delete(databaseDirectory, recursive: true);
        }
    }

    [Fact]
    public void CreateNewCatalog_ClearsCatalogDocumentsOnTheSharedWorkingDatabase()
    {
        var databaseDirectory = Path.Combine(Path.GetTempPath(), $"skycd-working-database-{Guid.NewGuid():N}");
        var databaseManager = new DatabaseManager();
        global::Couchbase.Lite.Database? database = null;
        try
        {
            database = databaseManager.Connect("default", databaseDirectory);
            var repositories = new RepositoryManager(databaseManager);
            var repository = Assert.IsType<CatalogDocumentRepository>(repositories.For<CatalogDocument>());
            repository.Save("root", new CatalogDocument
            {
                Id = "root",
                Name = "Root",
                Type = CatalogDocumentType.Folder
            });
            repository.Save("child", new CatalogDocument
            {
                Id = "child",
                ParentId = "root",
                Name = "File",
                Type = CatalogDocumentType.File
            });

            using (var catalog = MainWindowViewModel.CreateNewCatalog(repositories))
            {
                Assert.Empty(repository.GetAll());
                Assert.Empty(catalog.GetAllCatalogEntries());
            }

            Assert.Same(database, repository.Collection.Database);
            Assert.Empty(repository.GetAll());
        }
        finally
        {
            database?.Dispose();
            databaseManager.Dispose();
            if (Directory.Exists(databaseDirectory)) Directory.Delete(databaseDirectory, recursive: true);
        }
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
