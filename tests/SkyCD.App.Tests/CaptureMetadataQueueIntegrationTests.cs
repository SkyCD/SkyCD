using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Couchbase.Lite;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Menu;
using SkyCD.SDK.Capabilities.Sources;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Covers the capture import path (#1191): frames land under the capture folder the plugin reports and every
/// imported entry gets a durable metadata-queue marker, so extraction survives a restart and never runs twice
/// for the same entry.
/// </summary>
public sealed class CaptureMetadataQueueIntegrationTests : IDisposable
{
    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    [Fact]
    public void Enqueue_KeepsOneMarkerPerEntryId()
    {
        using var context = new TestContext();
        var first = DateTimeOffset.UtcNow;

        context.Queue.Enqueue("entry-1", first);
        context.Queue.Enqueue("entry-1", first.AddMinutes(5));

        var pending = Assert.Single(context.Queue.GetPending());
        Assert.Equal("entry-1", pending.EntryId);
        Assert.Equal(first, pending.EnqueuedAtUtc);
        Assert.Equal(0, pending.Attempts);
    }

    [Fact]
    public void Pending_DrainsOldestFirst()
    {
        using var context = new TestContext();
        var earliest = DateTimeOffset.UtcNow.AddMinutes(-3);
        context.Queue.Enqueue("entry-new", earliest.AddMinutes(2));
        context.Queue.Enqueue("entry-old", earliest);
        context.Queue.Enqueue("entry-mid", earliest.AddMinutes(1));

        Assert.Equal(
            ["entry-old", "entry-mid", "entry-new"],
            context.Queue.GetPending().Select(item => item.EntryId));
    }

    [Fact]
    public void RecordAttempt_CountsRetriesAndRemoveClearsTheMarker()
    {
        using var context = new TestContext();
        context.Queue.Enqueue("entry-1", DateTimeOffset.UtcNow);

        context.Queue.RecordAttempt("entry-1");
        context.Queue.RecordAttempt("entry-1");
        Assert.Equal(2, Assert.Single(context.Queue.GetPending()).Attempts);

        context.Queue.Remove("entry-1");
        Assert.Empty(context.Queue.GetPending());
        Assert.False(context.Queue.HasPending());
    }

    [Fact]
    public void Queue_SurvivesAReopenedRepository()
    {
        using var context = new TestContext();
        context.Queue.Enqueue("entry-1", DateTimeOffset.UtcNow);

        var reopened = (CaptureMetadataQueueRepository)context.Repositories.For<CaptureMetadataQueueDocument>();

        Assert.Equal("entry-1", Assert.Single(reopened.GetPending()).EntryId);
    }

    [Fact]
    public async Task Import_WritesEntriesUnderTheCaptureFolderAndQueuesMetadata()
    {
        using var context = new TestContext();
        using var viewModel = context.CreateViewModel();
        viewModel.CurrentCatalogPath = Path.Combine(context.MediaDirectory, "captures.scd");

        await viewModel.ImportSourceFilesAsync([BuildCapturedFile(context)]);

        var catalog = context.Catalog.GetAll();
        var capture = Assert.Single(catalog, entry =>
            entry.Name == "frame-1.jpg" && entry.Type == CatalogDocumentType.File);
        var dateFolder = catalog.Single(entry => entry.Id == capture.ParentId);
        var motion = catalog.Single(entry => entry.Id == dateFolder.ParentId);
        Assert.Equal("Motion", motion.Name);
        Assert.Equal("library", motion.ParentId);
        Assert.Equal(capture.Size, dateFolder.Size);
        Assert.Equal(capture.Size, motion.Size);
        Assert.Equal(
            Path.Combine(context.MediaDirectory, "frame-1.jpg"),
            capture.Properties[MediaSourcePathPropertyKey]);

        Assert.Equal(capture.Id, Assert.Single(context.Queue.GetPending()).EntryId);
    }

    [Fact]
    public async Task Import_WithoutAnOpenCatalog_ThrowsInsteadOfWriting()
    {
        using var context = new TestContext();
        using var viewModel = context.CreateViewModel();

        await Assert.ThrowsAsync<MotionCaptureCatalogUnavailableException>(
            () => viewModel.ImportSourceFilesAsync([BuildCapturedFile(context)]));

        Assert.False(viewModel.IsCaptureCatalogOpen);
        Assert.Empty(context.Queue.GetPending());
    }

    private const string MediaSourcePathPropertyKey = "FullName";

    private static SourceFile BuildCapturedFile(TestContext context)
    {
        var path = Path.Combine(context.MediaDirectory, "frame-1.jpg");
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, new byte[64]);
        }

        var info = new FileInfo(path);
        return new SourceFile(
            "Motion",
            info.LastWriteTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            info.Name,
            info.Length,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = info.Name,
                ["FullName"] = info.FullName,
                ["LastWriteTime"] = info.LastWriteTime.ToString("O", CultureInfo.InvariantCulture)
            });
    }

    private sealed class TestContext : IDisposable
    {
        private readonly Container container = new();
        private readonly TestWorkspace workspace = new();

        public TestContext()
        {
            new CouchbaseServiceRegistrator().RegisterServices(container);
            Repositories = container.Resolve<RepositoryManager>();
            Catalog = (CatalogDocumentRepository)Repositories.For<CatalogDocument>();
            Queue = (CaptureMetadataQueueRepository)Repositories.For<CaptureMetadataQueueDocument>();
            Catalog.Save("library", new CatalogDocument
            {
                Id = "library",
                Name = "Library",
                Type = CatalogDocumentType.Folder
            });

            MediaDirectory = workspace.Root;
        }

        public RepositoryManager Repositories { get; }

        public CatalogDocumentRepository Catalog { get; }

        public CaptureMetadataQueueRepository Queue { get; }

        public string MediaDirectory { get; }

        public MainWindowViewModel CreateViewModel() => new(Repositories);

        public void Dispose()
        {
            try
            {
                container.Resolve<DatabaseManager>().GetDatabase().Close();
            }
            catch (Exception exception) when (exception is CouchbaseException or InvalidOperationException)
            {
                // Already closed by the test.
            }

            container.Dispose();
            workspace.Dispose();
        }
    }
}
