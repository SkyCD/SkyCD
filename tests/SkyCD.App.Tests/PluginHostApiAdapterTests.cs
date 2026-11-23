using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Plugin.Abstractions.Capabilities.Storage;
using SkyCD.Presentation.Catalog;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class PluginHostApiAdapterTests
{
    [Fact]
    public async Task CatalogApiProjectsEntriesWithPathsKindsAndProperties()
    {
        using var workspace = new TestWorkspace();
        using var database = OpenDatabase(workspace.Root);
        var repository = OpenCatalog(database);
        repository.Save("library", new CatalogDocument { Id = "library", Name = "Library", Type = CatalogDocumentType.Folder });
        repository.Save("movies", new CatalogDocument
        {
            Id = "movies",
            Name = "Movies",
            ParentId = "library",
            Type = CatalogDocumentType.Folder
        });
        repository.Save("matrix", new CatalogDocument
        {
            Id = "matrix",
            Name = "The Matrix",
            ParentId = "movies",
            Type = CatalogDocumentType.Media,
            Size = 42
        });
        var state = new OpenCatalogState { Path = "catalog.cd" };
        state.Attach(repository);
        var api = new CatalogApiAdapter(state);

        var entries = await api.GetEntriesAsync();

        Assert.Equal("catalog.cd", api.OpenedCatalogPath);
        Assert.Equal(3, entries.Count);
        var movie = Assert.Single(entries, entry => entry.Id == "matrix");
        Assert.Equal("/Library/Movies/The Matrix", movie.Path);
        Assert.Equal(CatalogEntryKind.File, movie.Kind);
        Assert.Equal("movies", movie.ParentId);
        Assert.Equal(42, movie.Size);
        Assert.Null(await api.GetEntryAsync("missing"));
    }

    [Fact]
    public void StorageIsNamespacedPerPluginAndSurvivesReopen()
    {
        using var workspace = new TestWorkspace();
        using (var database = OpenDatabase(workspace.Root))
        {
            var state = AttachState(database);
            var notes = new PluginStorageApiFactory(state).ForPlugin("skycd.loans").OpenCollection("notes");
            notes.Upsert("one", new Dictionary<string, object?> { ["Text"] = "hello", ["Count"] = 3L });
            notes.Upsert("two", new Dictionary<string, object?> { ["Text"] = "other" });
            notes.Delete("two");

            Assert.Equal("hello", Assert.Single(notes.GetAll()).Fields["Text"]);
        }

        using (var reopened = OpenDatabase(workspace.Root))
        {
            var state = AttachState(reopened);
            var notes = new PluginStorageApiFactory(state).ForPlugin("skycd.loans").OpenCollection("notes");
            var record = Assert.IsType<PluginDocumentRecord>(notes.Get("one"));
            Assert.Equal("hello", record.Fields["Text"]);
            Assert.Equal(3L, Convert.ToInt64(record.Fields["Count"]));

            var other = new PluginStorageApiFactory(state).ForPlugin("skycd.other").OpenCollection("notes");
            Assert.Null(other.Get("one"));
            Assert.Empty(other.GetAll());
        }
    }

    [Fact]
    public void StorageRequiresAnOpenCatalog()
    {
        var state = new OpenCatalogState();
        Assert.Throws<PluginStorageUnavailableException>(() =>
            new PluginStorageApiFactory(state).ForPlugin("skycd.loans").OpenCollection("notes"));
    }

    private static OpenCatalogState AttachState(Database database)
    {
        var state = new OpenCatalogState();
        state.Attach(OpenCatalog(database));
        return state;
    }

    private static Database OpenDatabase(string directory) =>
        new("adapters", new DatabaseConfiguration { Directory = directory });

    private static CatalogDocumentRepository OpenCatalog(Database database)
    {
        var collection = database.GetDefaultCollection();
        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), collection.Name, collection);
        return repository;
    }
}
