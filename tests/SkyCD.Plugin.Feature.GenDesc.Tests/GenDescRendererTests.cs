using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.GenDesc.Exceptions;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.Parsing;
using SkyCD.Plugin.Feature.GenDesc.Rendering;
using SkyCD.SDK.Capabilities.GenDesc;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class GenDescRendererTests
{
    private static TemplateStore Store(params TemplateInfo[] templates) => new(templates);

    private static TemplateInfo Template(string id, string kind, string category, string language = "en",
        string mime = "video/*", string extensions = ".mkv", string body = "<% Default(\"Title\", Name) %>") =>
        new(id, kind, category, language, TemplateInfo.DefaultSite, "1", mime, extensions, body);

    private static GenDescEntryContext MovieContext(params (string Key, string Value)[] extra)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = "Example.Movie.2024.1080p",
            ["Title"] = "Example Movie",
            ["FileName"] = "Example.Movie.2024.1080p.mkv",
            ["MimeType"] = "video/x-matroska"
        };
        foreach (var pair in extra)
        {
            facts[pair.Key] = pair.Value;
        }

        return new GenDescEntryContext("Example.Movie.2024.1080p.mkv", ".mkv", "video/x-matroska", facts);
    }

    [Fact]
    public void RenderFillsTheTemplateAndReportsTheCategory()
    {
        var store = Store(Template("movie", TemplateInfo.DescriptionKind, "movie"));

        var result = GenDescRenderer.Render(store, MovieContext(), TemplateInfo.DescriptionKind, "en");

        Assert.Equal("Example Movie", result.Text);
        Assert.Equal("movie", result.Category);
        Assert.Equal("Movie", result.CategoryTitle);
        Assert.Equal("movie", result.Template.Id);
    }

    [Fact]
    public void LocalIdentityFactsWinOverHostValues()
    {
        var store = Store(Template("identity", TemplateInfo.DescriptionKind, "movie",
            body: "<% Name %>|<% FileName %>|<% Extension %>|<% MimeType %>|<% Category %>|<% CategoryTitle %>"));
        var context = MovieContext(("Name", "Wrong"), ("FileName", "Wrong.mkv"), ("Title", "Wrong"));

        var result = GenDescRenderer.Render(store, context, TemplateInfo.DescriptionKind, "en");

        Assert.Equal("Example.Movie.2024.1080p|Example.Movie.2024.1080p.mkv|.mkv|video/x-matroska|movie|Movie",
            result.Text);
    }

    [Fact]
    public void MissingTitleFallsBackToTheFileNameWithoutExtension()
    {
        var store = Store(Template("title", TemplateInfo.DescriptionKind, "movie", body: "<% Title %>"));
        var context = new GenDescEntryContext("Some.File.mkv", ".mkv", "video/x-matroska",
            new Dictionary<string, string>());

        var result = GenDescRenderer.Render(store, context, TemplateInfo.DescriptionKind, "en");

        Assert.Equal("Some.File", result.Text);
    }

    [Fact]
    public void MetadataFactsChooseTheTemplateCategory()
    {
        var store = Store(Template("tv", TemplateInfo.DescriptionKind, "tv"));

        var result = GenDescRenderer.Render(store, MovieContext(("Season", "1"), ("Episode", "2")),
            TemplateInfo.DescriptionKind, "en");

        Assert.Equal("tv", result.Category);
        Assert.Equal("TV Show", result.CategoryTitle);
    }

    [Fact]
    public void RequestedLanguageIsPreferred()
    {
        var store = Store(
            Template("english", TemplateInfo.DescriptionKind, "movie"),
            Template("lithuanian", TemplateInfo.DescriptionKind, "movie", "lt"));

        Assert.Equal("lithuanian",
            GenDescRenderer.Render(store, MovieContext(), TemplateInfo.DescriptionKind, "lt").Template.Id);
        Assert.Equal("english",
            GenDescRenderer.Render(store, MovieContext(), TemplateInfo.DescriptionKind, "en").Template.Id);
    }

    [Fact]
    public void MissingTemplateKindThrows()
    {
        var store = Store(Template("description", TemplateInfo.DescriptionKind, "movie"));

        var exception = Assert.Throws<GenDescGenerationException>(() =>
            GenDescRenderer.Render(store, MovieContext(), TemplateInfo.NfoKind, "en"));

        Assert.Contains("no nfo template", exception.Message);
    }

    [Fact]
    public void EmptyRenderingThrows()
    {
        var store = Store(Template("empty", TemplateInfo.DescriptionKind, "movie", body: "<% Missing %>   "));

        var exception = Assert.Throws<GenDescGenerationException>(() =>
            GenDescRenderer.Render(store, MovieContext(), TemplateInfo.DescriptionKind, "en"));

        Assert.Contains("rendered no text", exception.Message);
    }

    [Fact]
    public async Task FromFileAsyncDescribesTheLocalFile()
    {
        using var workspace = new TestWorkspace();
        var path = workspace.CreateFile("Shows/Example.Movie.mkv", new byte[2048]);

        var context = await GenDescRenderer.FromFileAsync(path);

        Assert.Equal("Example.Movie.mkv", context.FileName);
        Assert.Equal(".mkv", context.Extension);
        Assert.Equal("video/x-matroska", context.MimeType);
        Assert.Equal("Example.Movie", context.Facts["Name"]);
        Assert.Equal("2048", context.Facts["SizeBytes"]);
        Assert.Equal("2 KB", context.Facts["Size"]);
        Assert.Equal(Path.GetFullPath(path), context.Facts["Path"]);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(path)), context.Facts["Directory"]);
    }

    [Fact]
    public async Task FromFileAsyncRejectsAMissingFile()
    {
        using var workspace = new TestWorkspace();

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            GenDescRenderer.FromFileAsync(workspace.GetPath("absent.mkv")));
    }
}
