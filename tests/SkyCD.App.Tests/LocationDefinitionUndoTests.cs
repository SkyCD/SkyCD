using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Locations;
using SkyCD.Presentation.UndoRedo;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class LocationDefinitionUndoTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-locationundo-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public LocationDefinitionUndoTests()
    {
        Directory.CreateDirectory(appDataRoot);
        Environment.SetEnvironmentVariable("APPDATA", appDataRoot);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", appDataRoot);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", Path.Combine(appDataRoot, "SkyCD"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", previousAppData);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousXdgConfig);
        Environment.SetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY", previousDatabaseDirectory);
        try
        {
            if (Directory.Exists(appDataRoot))
            {
                Directory.Delete(appDataRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed class Stack : IDisposable
    {
        public Stack()
        {
            Container = new Container();
            new CouchbaseServiceRegistrator().RegisterServices(Container);
            var repositories = Container.Resolve<RepositoryManager>();
            Catalog = (CatalogDocumentRepository)repositories.For<CatalogDocument>();
            Locations = (LocationDocumentRepository)repositories.For<LocationDocument>();
            Service = new CatalogLocationService(Catalog, Locations);
            Undo = new CatalogUndoManager(Catalog);
        }

        public Container Container { get; }
        public CatalogDocumentRepository Catalog { get; }
        public LocationDocumentRepository Locations { get; }
        public CatalogLocationService Service { get; }
        public CatalogUndoManager Undo { get; }

        public void Dispose() => Container.Dispose();
    }

    private static void SeedEntry(CatalogDocumentRepository catalog, string id)
    {
        catalog.Save(id, new CatalogDocument
        {
            Id = id,
            Name = id,
            Type = SkyCD.Documents.Enum.CatalogDocumentType.File,
            Properties = new PropertiesCollection()
        });
    }

    private static Dictionary<string, string?> OwnOf(CatalogLocationService service, params string[] entryIds) =>
        entryIds.ToDictionary(id => id, id => service.GetEntryOwnLocationId(id));

    [Fact]
    public async System.Threading.Tasks.Task CreateLocation_UndoRemovesAndRedoRestores()
    {
        using var stack = new Stack();
        var location = stack.Service.CreateLocation("Blue");
        stack.Undo.RecordLocationCreated(stack.Locations, stack.Catalog, location.Id, location.Name);

        Assert.NotNull(stack.Locations.Get(location.Id));

        await stack.Undo.UndoAsync();
        Assert.Null(stack.Locations.Get(location.Id));

        await stack.Undo.RedoAsync();
        Assert.Equal("Blue", stack.Locations.Get(location.Id)?.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task RenameLocation_UndoRestoresOldName()
    {
        using var stack = new Stack();
        var location = stack.Service.CreateLocation("Blue");
        stack.Service.RenameLocation(location.Id, "Azure");
        stack.Undo.RecordLocationRenamed(stack.Locations, location.Id, "Blue", "Azure");

        Assert.Equal("Azure", stack.Locations.Get(location.Id)?.Name);

        await stack.Undo.UndoAsync();
        Assert.Equal("Blue", stack.Locations.Get(location.Id)?.Name);

        await stack.Undo.RedoAsync();
        Assert.Equal("Azure", stack.Locations.Get(location.Id)?.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteLocation_UndoRestoresLocationAndAssignments()
    {
        using var stack = new Stack();
        SeedEntry(stack.Catalog, "e1");
        SeedEntry(stack.Catalog, "e2");
        var location = stack.Service.CreateLocation("Blue");
        stack.Service.SetEntryLocation("e1", location.Id);
        stack.Service.SetEntryLocation("e2", location.Id);
        var affected = OwnOf(stack.Service, "e1", "e2");

        stack.Service.DeleteLocation(location.Id);
        stack.Undo.RecordLocationDeleted(stack.Catalog, stack.Locations, location.Id, location.Name, affected);

        Assert.Null(stack.Locations.Get(location.Id));
        Assert.Null(stack.Service.GetEntryOwnLocationId("e1"));

        await stack.Undo.UndoAsync();
        Assert.Equal("Blue", stack.Locations.Get(location.Id)?.Name);
        Assert.Equal(location.Id, stack.Service.GetEntryOwnLocationId("e1"));
        Assert.Equal(location.Id, stack.Service.GetEntryOwnLocationId("e2"));

        await stack.Undo.RedoAsync();
        Assert.Null(stack.Locations.Get(location.Id));
        Assert.Null(stack.Service.GetEntryOwnLocationId("e1"));
    }

    [Fact]
    public async System.Threading.Tasks.Task MergeLocations_UndoRestoresSourceAndAssignments()
    {
        using var stack = new Stack();
        SeedEntry(stack.Catalog, "e1");
        SeedEntry(stack.Catalog, "e2");
        var source = stack.Service.CreateLocation("Old");
        var target = stack.Service.CreateLocation("New");
        stack.Service.SetEntryLocation("e1", source.Id);
        stack.Service.SetEntryLocation("e2", target.Id);
        var affected = OwnOf(stack.Service, "e1", "e2");

        stack.Service.MergeLocations(source.Id, target.Id);
        stack.Undo.RecordLocationsMerged(stack.Catalog, stack.Locations, source.Id, source.Name, target.Id, affected);

        Assert.Null(stack.Locations.Get(source.Id));
        Assert.Equal(target.Id, stack.Service.GetEntryOwnLocationId("e1"));

        await stack.Undo.UndoAsync();
        Assert.Equal("Old", stack.Locations.Get(source.Id)?.Name);
        Assert.Equal(source.Id, stack.Service.GetEntryOwnLocationId("e1"));
        Assert.Equal(target.Id, stack.Service.GetEntryOwnLocationId("e2"));

        await stack.Undo.RedoAsync();
        Assert.Null(stack.Locations.Get(source.Id));
        Assert.Equal(target.Id, stack.Service.GetEntryOwnLocationId("e1"));
        Assert.Equal(target.Id, stack.Service.GetEntryOwnLocationId("e2"));
    }
}
