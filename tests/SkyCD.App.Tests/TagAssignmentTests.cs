using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Tags;
using SkyCD.Presentation.UndoRedo;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class TagAssignmentTests : IDisposable
{
    private readonly string appDataRoot = Path.Combine(Path.GetTempPath(), $"skycd-tags-{Guid.NewGuid():N}");
    private readonly string? previousAppData = Environment.GetEnvironmentVariable("APPDATA");
    private readonly string? previousXdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? previousDatabaseDirectory = Environment.GetEnvironmentVariable("SKYCD_DATABASE_DIRECTORY");

    public TagAssignmentTests()
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
        }

        public Container Container { get; }

        public CatalogDocumentRepository Catalog { get; }

        public TagDocumentRepository Tags { get; }

        public CatalogTagService Service { get; }

        public void Dispose() => Container.Dispose();
    }

    private static Stack CreateStack() => new();

    private static CatalogDocument Entry(string id, string? parentId = null)
    {
        return new CatalogDocument
        {
            Id = id,
            Name = id,
            ParentId = parentId,
            Type = CatalogDocumentType.File,
            Size = 100,
            ChildrenCount = 0
        };
    }

    private static CatalogDocument LegacyEntry(string id, string? parentId = null)
    {
        // Entries written before tag storage existed carry no Tags property.
        return new CatalogDocument
        {
            Id = id,
            Name = id,
            ParentId = parentId,
            Type = CatalogDocumentType.Folder,
            Size = 0,
            ChildrenCount = 0,
            Properties = new PropertiesCollection(new Dictionary<string, object?>
            {
                ["Comment"] = "legacy"
            })
        };
    }

    [Fact]
    public void Migration_LegacyEntriesLoadUnchangedAndNormalizeCleanly()
    {
        using (var stack = CreateStack())
        {
            stack.Catalog.Save("root", LegacyEntry("root"));
            stack.Catalog.Save("child", LegacyEntry("child", "root"));
        }

        using var recreated = CreateStack();
        var service = recreated.Service;
        Assert.Empty(service.GetTags());
        Assert.Empty(service.GetEntryTagIds("child"));
        Assert.Equal("legacy", recreated.Catalog.Get("child")!.Properties["Comment"]);
        Assert.Equal(0, service.NormalizeAllEntries());
        Assert.False(recreated.Catalog.Get("child")!.Properties.ContainsKey("Tags"));
    }

    [Fact]
    public void Assignments_SurviveRepositoryRecreationAndRestart()
    {
        using (var stack = CreateStack())
        {
            stack.Catalog.Save("root", Entry("root"));
            stack.Catalog.Save("file", Entry("file", "root"));
            var favorite = stack.Service.CreateTag("Favorites");
            var review = stack.Service.CreateTag("Review");
            stack.Service.AssignTags(["file", "root"], [favorite.Id]);
            stack.Service.AssignTags(["file"], [review.Id]);
        }

        using var reopened = CreateStack();
        var service = reopened.Service;
        Assert.Equal(["tag-favorites", "tag-review"], service.GetEntryTagIds("file"));
        Assert.Equal(["tag-favorites"], service.GetEntryTagIds("root"));
        Assert.Equal("Review", service.GetEntryTags("file").Single(tag => tag.Id == "tag-review").Name);
        Assert.Equal("Favorites", service.GetTags().Single(tag => tag.Id == "tag-favorites").Name);
    }

    [Fact]
    public void DuplicateNames_AreRejectedCaseInsensitively()
    {
        using var stack = CreateStack();
        stack.Service.CreateTag("Favorites");

        Assert.Throws<TagDuplicateNameException>(() => stack.Service.CreateTag("favorites"));
        Assert.Throws<TagDuplicateNameException>(() => stack.Service.CreateTag("  FAVORITES  "));
        Assert.Throws<TagNameRequiredException>(() => stack.Service.CreateTag("   "));
    }

    [Fact]
    public void Rename_KeepsAssignmentsByStableId()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("file", Entry("file"));
        var tag = stack.Service.CreateTag("Favorites");
        stack.Service.AssignTags(["file"], [tag.Id]);

        stack.Service.RenameTag(tag.Id, "Best Ever");

        Assert.Equal("Best Ever", stack.Service.GetTags().Single().Name);
        Assert.Equal([tag.Id], stack.Service.GetEntryTagIds("file"));
        Assert.Throws<TagDuplicateNameException>(() =>
        {
            var other = stack.Service.CreateTag("Other");
            stack.Service.RenameTag(other.Id, "best ever");
        });
    }

    [Fact]
    public void Merge_MovesAssignmentsDeletesSourceAndLeavesNoOrphans()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", Entry("a"));
        stack.Catalog.Save("b", Entry("b"));
        var source = stack.Service.CreateTag("Old");
        var target = stack.Service.CreateTag("New");
        stack.Service.AssignTags(["a"], [source.Id]);
        stack.Service.AssignTags(["b"], [source.Id, target.Id]);

        stack.Service.MergeTags(source.Id, target.Id);

        Assert.Equal([target.Id], stack.Service.GetEntryTagIds("a"));
        Assert.Equal([target.Id], stack.Service.GetEntryTagIds("b"));
        Assert.DoesNotContain(stack.Service.GetTags(), tag => tag.Id == source.Id);
        Assert.Empty(stack.Service.GetEntriesWithTag(source.Id));
        Assert.Equal(2, stack.Service.GetEntriesWithTag(target.Id).Count);
        Assert.Throws<TagNotFoundException>(() => stack.Service.MergeTags(source.Id, target.Id));
    }

    [Fact]
    public void DeleteTag_SweepsAssignmentsConsistently()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", Entry("a"));
        stack.Catalog.Save("b", Entry("b"));
        var doomed = stack.Service.CreateTag("Temp");
        var kept = stack.Service.CreateTag("Kept");
        stack.Service.AssignTags(["a", "b"], [doomed.Id, kept.Id]);

        stack.Service.DeleteTag(doomed.Id);

        Assert.Equal([kept.Id], stack.Service.GetEntryTagIds("a"));
        Assert.Equal([kept.Id], stack.Service.GetEntryTagIds("b"));
        Assert.DoesNotContain(stack.Service.GetTags(), tag => tag.Id == doomed.Id);
        Assert.Throws<TagNotFoundException>(() => stack.Service.DeleteTag(doomed.Id));
    }

    [Fact]
    public void BatchAssignAndUnassign_WorkAcrossEntries()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", Entry("a"));
        stack.Catalog.Save("b", Entry("b"));
        var one = stack.Service.CreateTag("One");
        var two = stack.Service.CreateTag("Two");

        stack.Service.AssignTags(["a", "b", "missing"], [one.Id, two.Id]);
        Assert.Equal([one.Id, two.Id], stack.Service.GetEntryTagIds("a"));
        Assert.Equal([one.Id, two.Id], stack.Service.GetEntryTagIds("b"));

        stack.Service.UnassignTags(["a"], [one.Id]);
        Assert.Equal([two.Id], stack.Service.GetEntryTagIds("a"));
        Assert.Equal([one.Id, two.Id], stack.Service.GetEntryTagIds("b"));
    }

    [Fact]
    public void UnknownTagIds_AreRejectedWithoutChangingEntries()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", Entry("a"));

        Assert.Throws<TagNotFoundException>(() => stack.Service.SetEntryTags("a", ["tag-nope"]));
        Assert.Throws<TagNotFoundException>(() => stack.Service.AssignTags(["a"], ["tag-nope"]));
        Assert.Empty(stack.Service.GetEntryTagIds("a"));
    }

    [Fact]
    public async System.Threading.Tasks.Task BatchChanges_UndoAndRedoThroughHistory()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", Entry("a"));
        stack.Catalog.Save("b", Entry("b"));
        var one = stack.Service.CreateTag("One");
        var two = stack.Service.CreateTag("Two");
        using var undo = new CatalogUndoManager(stack.Catalog);

        var before = new Dictionary<string, (IReadOnlyList<string>, IReadOnlyList<string>)>
        {
            ["a"] = (stack.Service.GetEntryTagIds("a"), [one.Id]),
            ["b"] = (stack.Service.GetEntryTagIds("b"), [one.Id, two.Id])
        };
        stack.Service.AssignTags(["a", "b"], [one.Id]);
        stack.Service.AssignTags(["b"], [two.Id]);
        undo.RecordTagsBatchChange(before.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value.Item1, stack.Service.GetEntryTagIds(pair.Key))));

        await undo.UndoAsync();
        Assert.Empty(stack.Service.GetEntryTagIds("a"));
        Assert.Empty(stack.Service.GetEntryTagIds("b"));

        await undo.RedoAsync();
        Assert.Equal([one.Id], stack.Service.GetEntryTagIds("a"));
        Assert.Equal([one.Id, two.Id], stack.Service.GetEntryTagIds("b"));
    }

    [Fact]
    public void RemoveMissingTagReferences_CleansDanglingAssignments()
    {
        using var stack = CreateStack();
        stack.Catalog.Save("a", Entry("a"));
        var kept = stack.Service.CreateTag("Kept");
        stack.Service.AssignTags(["a"], [kept.Id]);

        var entry = stack.Catalog.Get("a")!;
        var tampered = new PropertiesCollection(entry.Properties)
        {
            ["Tags"] = new List<string> { kept.Id, "tag-ghost" }
        };
        stack.Catalog.Save("a", new CatalogDocument
        {
            Id = entry.Id,
            Name = entry.Name,
            ParentId = entry.ParentId,
            Type = entry.Type,
            Size = entry.Size,
            ChildrenCount = entry.ChildrenCount,
            ContentsState = entry.ContentsState,
            Status = entry.Status,
            Properties = tampered
        });

        Assert.Equal(1, stack.Service.RemoveMissingTagReferences());
        Assert.Equal([kept.Id], stack.Service.GetEntryTagIds("a"));
        Assert.Equal(0, stack.Service.RemoveMissingTagReferences());
    }
}
