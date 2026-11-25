using System;
using System.Threading.Tasks;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Catalog;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Covers the cross-thread catalog access that stalled the test host before #1117: Couchbase Lite deadlocks
/// when a query runs while another thread holds a batch transaction open on the same database, which is the
/// shape of a background size recount racing an undo. Every query and batch goes through
/// <see cref="DatabaseGate"/>, so the same work must now finish.
/// </summary>
public sealed class DatabaseGateConcurrencyTests : IDisposable
{
    private const int Iterations = 100;

    private readonly AppDataSandbox sandbox = new();

    public void Dispose() => sandbox.Dispose();

    [Fact]
    public async Task QueriesBatchesAndSizeRecounts_RunConcurrentlyWithoutDeadlock()
    {
        using var container = new Container();
        new CouchbaseServiceRegistrator().RegisterServices(container);
        var repositories = container.Resolve<RepositoryManager>();
        var catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
        catalog.Save("root", new CatalogDocument { Id = "root", Name = "Root", Type = CatalogDocumentType.Folder });

        var workers = new[]
        {
            Repeat(() => _ = catalog.GetAll()),
            Repeat(() => _ = catalog.GetChildrenOf("root")),
            Repeat(() => DatabaseGate.InBatch(catalog.Collection.Database, () =>
                catalog.Save("batch-child", new CatalogDocument
                {
                    Id = "batch-child",
                    Name = "Batch Child",
                    ParentId = "root",
                    Type = CatalogDocumentType.File
                }))),
            Repeat(() => CatalogSizeMaintenance.RecalculateAncestors(catalog, ["root"]))
        };

        var all = Task.WhenAll(workers);
        var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(60))) == all;
        Assert.True(completed, "concurrent catalog queries, batches and size recounts deadlocked");
        await all;
    }

    private static Task Repeat(Action work) => Task.Run(() =>
    {
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            work();
        }
    });
}
