using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Feature.GenDesc.Exceptions;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.Parsing;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class TemplateStoreTests
{
    private static TemplateInfo Template(string id, string kind, string category, string language = "en",
        string mime = "", string extensions = "") =>
        new(id, kind, category, language, TemplateInfo.DefaultSite, "1", mime, extensions, "body of " + id);

    [Fact]
    public void ListsTemplatesOrderedByKindCategoryAndId()
    {
        var store = new TemplateStore(
        [
            Template("b", TemplateInfo.NfoKind, "tv"),
            Template("a", TemplateInfo.NfoKind, "movie"),
            Template("c", TemplateInfo.DescriptionKind, "movie")
        ]);

        Assert.Equal(["c", "a", "b"], store.Templates.Select(template => template.Id).ToArray());
        Assert.Equal(["a", "b"], store.ForKind(TemplateInfo.NfoKind).Select(template => template.Id).ToArray());
        Assert.Single(store.ForKind("unknown"));
    }

    [Fact]
    public void FindIsCaseInsensitiveAndIgnoresBlankIds()
    {
        var store = new TemplateStore([Template("Alpha", TemplateInfo.DescriptionKind, "movie")]);

        Assert.NotNull(store.Find("alpha"));
        Assert.Null(store.Find("missing"));
        Assert.Null(store.Find(" "));
        Assert.Null(store.Find(null));
    }

    [Fact]
    public void SecondTemplateWithSameIdWins()
    {
        var store = new TemplateStore(
        [
            Template("same", TemplateInfo.DescriptionKind, "movie"),
            Template("same", TemplateInfo.DescriptionKind, "tv")
        ]);

        Assert.Equal("tv", store.Find("same")!.Category);
    }

    [Fact]
    public void LoadOverlaysUserFilesOnBuiltIns()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.GetPath("templates");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "custom.tpl"),
            "## kind=description\n## category=movie\n## ext=.mkv\ncustom body");

        var store = TemplateStore.Load(directory);

        Assert.True(store.Templates.Count > 1);
        Assert.Equal("custom body", store.Find("custom")!.Body);
        Assert.Equal(directory, store.Directory);
        Assert.Empty(store.LoadErrors);
    }

    [Fact]
    public void LoadAcceptsAMissingDirectory()
    {
        using var workspace = new TestWorkspace();
        var store = TemplateStore.Load(workspace.GetPath("absent"));

        Assert.NotEmpty(store.Templates);
        Assert.Empty(store.LoadErrors);
        Assert.NotEmpty(TemplateStore.Load(" ").Templates);
    }

    [Fact]
    public void SeedWritesBuiltInsOnceAndKeepsUserEdits()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.GetPath("templates");
        var store = TemplateStore.Load(null);

        var written = store.SeedUserTemplates(directory);
        var expected = store.Templates.Count(template => template.Id.StartsWith("default-", StringComparison.Ordinal));
        Assert.Equal(expected, written);
        Assert.Equal(expected, Directory.GetFiles(directory, "*.tpl").Length);

        var edited = Path.Combine(directory, "default-description-movie.tpl");
        File.WriteAllText(edited, "edited by the user");
        Assert.Equal(0, store.SeedUserTemplates(directory));
        Assert.Equal("edited by the user", File.ReadAllText(edited));
    }

    [Fact]
    public void ExplicitIdWinsOnlyWhenTheKindMatches()
    {
        var store = new TemplateStore(
        [
            Template("pinned", TemplateInfo.DescriptionKind, "movie"),
            Template("other", TemplateInfo.NfoKind, "movie")
        ]);

        Assert.Equal("pinned", store.Select(TemplateInfo.DescriptionKind, null, null, null, "en", "pinned")!.Id);
        Assert.Equal("other", store.Select(TemplateInfo.NfoKind, null, null, null, "en", "pinned")!.Id);
    }

    [Fact]
    public void SpecificCoverageBeatsLanguageAndDefaultCategory()
    {
        var store = new TemplateStore(
        [
            Template("specific", TemplateInfo.DescriptionKind, "movie", "en", "video/*", ".mkv"),
            Template("generic", TemplateInfo.DescriptionKind, "movie", "lt")
        ]);

        var selected = store.Select(TemplateInfo.DescriptionKind, "video/x-matroska", ".mkv", "movie", "lt");

        Assert.Equal("specific", selected!.Id);
    }

    [Fact]
    public void LanguageBreaksTiesBetweenEquallySpecificTemplates()
    {
        var store = new TemplateStore(
        [
            Template("english", TemplateInfo.DescriptionKind, "movie", "en", "video/*", ".mkv"),
            Template("lithuanian", TemplateInfo.DescriptionKind, "movie", "lt", "video/*", ".mkv")
        ]);

        Assert.Equal("lithuanian",
            store.Select(TemplateInfo.DescriptionKind, "video/x-matroska", ".mkv", "movie", "lt")!.Id);
        Assert.Equal("english",
            store.Select(TemplateInfo.DescriptionKind, "video/x-matroska", ".mkv", "movie", "en")!.Id);
    }

    [Fact]
    public void FallbackCategoryScoresLowButStillSelects()
    {
        var store = new TemplateStore(
        [
            Template("general", TemplateInfo.DescriptionKind, TemplateInfo.GeneralCategory),
            Template("movie", TemplateInfo.DescriptionKind, "movie")
        ]);

        Assert.Equal("movie", store.Select(TemplateInfo.DescriptionKind, null, null, "movie", "en")!.Id);
        Assert.Equal("general", store.Select(TemplateInfo.DescriptionKind, null, null, "unknown", "en")!.Id);
    }

    [Fact]
    public void SelectReturnsNullWhenNoTemplateMatchesTheKind()
    {
        var store = new TemplateStore([Template("only", TemplateInfo.NfoKind, "movie")]);

        Assert.Null(store.Select(TemplateInfo.DescriptionKind, null, null, "movie", "en"));
    }

    [Fact]
    public void SaveWritesAFileAndReplacesTheMemoryCopy()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.GetPath("templates");
        var store = TemplateStore.Load(directory);
        var template = Template("saved", TemplateInfo.DescriptionKind, "movie");

        store.Save(template);

        var path = Path.Combine(directory, "saved.tpl");
        Assert.True(File.Exists(path));
        Assert.Equal(template, store.Find("saved"));
        Assert.Contains("## id=saved", File.ReadAllText(path));
    }

    [Fact]
    public void SaveRejectsAnInMemoryStoreAndUnsafeIds()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.GetPath("templates");
        var store = TemplateStore.Load(directory);

        Assert.Throws<TemplateStorageException>(() => TemplateStore.Load(null)
            .Save(Template("x", TemplateInfo.DescriptionKind, "movie")));
        Assert.Throws<TemplateStorageException>(() => store.Save(Template("../escape", TemplateInfo.DescriptionKind, "movie")));
        Assert.Throws<TemplateStorageException>(() => store.Save(Template("bad/name", TemplateInfo.DescriptionKind, "movie")));
    }

    [Fact]
    public void DeleteRemovesUserTemplatesAndRestoresBuiltIns()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.GetPath("templates");
        var store = TemplateStore.Load(directory);
        store.SeedUserTemplates(directory);

        var custom = Template("custom", TemplateInfo.DescriptionKind, "movie");
        store.Save(custom);
        Assert.True(store.Delete(custom));
        Assert.Null(store.Find("custom"));
        Assert.False(File.Exists(Path.Combine(directory, "custom.tpl")));

        var builtIn = store.Find("default-description-movie")!;
        Assert.True(store.Delete(builtIn));
        Assert.Equal(builtIn, store.Find("default-description-movie"));
        Assert.False(File.Exists(Path.Combine(directory, "default-description-movie.tpl")));
    }

    [Fact]
    public void DeleteOfAMissingUserTemplateReportsNoFile()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.GetPath("templates");
        var store = TemplateStore.Load(directory);
        var custom = Template("never-saved", TemplateInfo.DescriptionKind, "movie");
        store.Save(custom);
        File.Delete(Path.Combine(directory, "never-saved.tpl"));

        Assert.False(store.Delete(custom));
        Assert.Null(store.Find("never-saved"));
    }
}
