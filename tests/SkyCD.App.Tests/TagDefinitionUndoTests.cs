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
using SkyCD.Presentation.Tags;
using SkyCD.Presentation.UndoRedo;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class TagDefinitionUndoTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-tagundo-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public TagDefinitionUndoTests()
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
            Tags = (TagDocumentRepository)repositories.For<TagDocument>();
            Service = new CatalogTagService(Catalog, Tags);
            Undo = new CatalogUndoManager(Catalog);
        }

        public Container Container { get; }
        public CatalogDocumentRepository Catalog { get; }
        public TagDocumentRepository Tags { get; }
        public CatalogTagService Service { get; }
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

    private static IReadOnlyList<string> Assigned(CatalogTagService service, string entryId) =>
        service.GetEntryTagIds(entryId);

    [Fact]
    public async System.Threading.Tasks.Task CreateTag_UndoRemovesAndRedoRestores()
    {
        using var stack = new Stack();
        var tag = stack.Service.CreateTag("Blue");
        stack.Undo.RecordTagCreated(stack.Tags, stack.Catalog, tag.Id, tag.Name);

        Assert.NotNull(stack.Tags.Get(tag.Id));

        await stack.Undo.UndoAsync();
        Assert.Null(stack.Tags.Get(tag.Id));

        await stack.Undo.RedoAsync();
        Assert.Equal("Blue", stack.Tags.Get(tag.Id)?.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task RenameTag_UndoRestoresOldName()
    {
        using var stack = new Stack();
        var tag = stack.Service.CreateTag("Blue");
        stack.Service.RenameTag(tag.Id, "Azure");
        stack.Undo.RecordTagRenamed(stack.Tags, tag.Id, "Blue", "Azure");

        Assert.Equal("Azure", stack.Tags.Get(tag.Id)?.Name);

        await stack.Undo.UndoAsync();
        Assert.Equal("Blue", stack.Tags.Get(tag.Id)?.Name);

        await stack.Undo.RedoAsync();
        Assert.Equal("Azure", stack.Tags.Get(tag.Id)?.Name);
    }

    [Fact]
    public async System.Threading.Tasks.Task DeleteTag_UndoRestoresTagAndAssignments()
    {
        using var stack = new Stack();
        SeedEntry(stack.Catalog, "e1");
        SeedEntry(stack.Catalog, "e2");
        var tag = stack.Service.CreateTag("Blue");
        stack.Service.AssignTags(["e1", "e2"], [tag.Id]);
        var affected = new Dictionary<string, IReadOnlyList<string>>
        {
            ["e1"] = Assigned(stack.Service, "e1"),
            ["e2"] = Assigned(stack.Service, "e2")
        };

        stack.Service.DeleteTag(tag.Id);
        stack.Undo.RecordTagDeleted(stack.Catalog, stack.Tags, tag.Id, tag.Name, affected);

        Assert.Null(stack.Tags.Get(tag.Id));
        Assert.Empty(Assigned(stack.Service, "e1"));

        await stack.Undo.UndoAsync();
        Assert.Equal("Blue", stack.Tags.Get(tag.Id)?.Name);
        Assert.Equal([tag.Id], Assigned(stack.Service, "e1"));
        Assert.Equal([tag.Id], Assigned(stack.Service, "e2"));

        await stack.Undo.RedoAsync();
        Assert.Null(stack.Tags.Get(tag.Id));
        Assert.Empty(Assigned(stack.Service, "e1"));
    }

    [Fact]
    public async System.Threading.Tasks.Task MergeTags_UndoRestoresSourceAndAssignments()
    {
        using var stack = new Stack();
        SeedEntry(stack.Catalog, "e1");
        SeedEntry(stack.Catalog, "e2");
        var source = stack.Service.CreateTag("Old");
        var target = stack.Service.CreateTag("New");
        stack.Service.AssignTags(["e1"], [source.Id]);
        stack.Service.AssignTags(["e2"], [target.Id]);
        var affected = new Dictionary<string, IReadOnlyList<string>>
        {
            ["e1"] = Assigned(stack.Service, "e1"),
            ["e2"] = Assigned(stack.Service, "e2")
        };

        stack.Service.MergeTags(source.Id, target.Id);
        stack.Undo.RecordTagsMerged(stack.Catalog, stack.Tags, source.Id, source.Name, target.Id, affected);

        Assert.Null(stack.Tags.Get(source.Id));
        Assert.Equal([target.Id], Assigned(stack.Service, "e1"));

        await stack.Undo.UndoAsync();
        Assert.Equal("Old", stack.Tags.Get(source.Id)?.Name);
        Assert.Equal([source.Id], Assigned(stack.Service, "e1"));
        Assert.Equal([target.Id], Assigned(stack.Service, "e2"));

        await stack.Undo.RedoAsync();
        Assert.Null(stack.Tags.Get(source.Id));
        Assert.Equal([target.Id], Assigned(stack.Service, "e1"));
        Assert.Equal([target.Id], Assigned(stack.Service, "e2"));
    }
}

