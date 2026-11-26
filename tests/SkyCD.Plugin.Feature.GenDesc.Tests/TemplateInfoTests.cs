using System.Linq;
using SkyCD.Plugin.Feature.GenDesc.Models;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class TemplateInfoTests
{
    [Fact]
    public void ParseReadsHeaderFieldsAndTrimsTheBody()
    {
        const string content = """
            ## id=My.Template
            ## kind=NFO
            ## category=Movie
            ## site=Tracker
            ## lang=LT
            ## version=2
            ## mime=video/x-matroska, video/mp4
            ## ext=MKV; mp4
            body line
            """;

        var template = TemplateInfo.Parse(content, "fallback");

        Assert.Equal("my.template", template.Id);
        Assert.Equal(TemplateInfo.NfoKind, template.Kind);
        Assert.Equal("movie", template.Category);
        Assert.Equal("tracker", template.Site);
        Assert.Equal("lt", template.Language);
        Assert.Equal("2", template.Version);
        Assert.Equal("video/x-matroska video/mp4", template.MimeTypes);
        Assert.Equal([".mkv", ".mp4"], template.ExtensionList.ToArray());
        Assert.Equal("body line", template.Body);
    }

    [Fact]
    public void ParseFillsMissingHeadersFromFallbacks()
    {
        var template = TemplateInfo.Parse("just a body", "custom", TemplateInfo.NfoKind, "music", "lt");

        Assert.Equal("custom", template.Id);
        Assert.Equal(TemplateInfo.NfoKind, template.Kind);
        Assert.Equal("music", template.Category);
        Assert.Equal("lt", template.Language);
        Assert.Equal(TemplateInfo.DefaultSite, template.Site);
        Assert.Equal("1", template.Version);
        Assert.Empty(template.MimeList);
        Assert.Equal("just a body", template.Body);
    }

    [Fact]
    public void ParseNormalizesUnknownKindToDescription()
    {
        Assert.Equal(TemplateInfo.DescriptionKind, TemplateInfo.Parse("## kind=weird\nx", "id").Kind);
        Assert.Equal(TemplateInfo.DescriptionKind, TemplateInfo.NormalizeKind("weird"));
        Assert.Equal(TemplateInfo.NfoKind, TemplateInfo.NormalizeKind(" NFO "));
    }

    [Fact]
    public void FileContentRoundTrips()
    {
        var original = new TemplateInfo("id", TemplateInfo.DescriptionKind, "movie", "lt", "site", "3",
            "video/mp4", ".mp4 .mkv", "line one\nline two");

        var parsed = TemplateInfo.Parse(original.ToFileContent(), "fallback");

        Assert.Equal(original, parsed);
    }

    [Fact]
    public void MatchesMimeHandlesWildcardsAndEmptyLists()
    {
        var template = new TemplateInfo("id", TemplateInfo.DescriptionKind, "music", "en",
            TemplateInfo.DefaultSite, "1", "audio/* video/mp4", string.Empty, "body");

        Assert.True(template.MatchesMime("audio/flac"));
        Assert.True(template.MatchesMime("VIDEO/MP4"));
        Assert.False(template.MatchesMime("application/pdf"));
        Assert.False(template.MatchesMime(null));

        var open = template with { MimeTypes = string.Empty };
        Assert.True(open.MatchesMime(null));
    }

    [Fact]
    public void MatchesExtensionNormalizesTheDot()
    {
        var template = new TemplateInfo("id", TemplateInfo.DescriptionKind, "movie", "en",
            TemplateInfo.DefaultSite, "1", string.Empty, ".mkv .MP4", "body");

        Assert.True(template.MatchesExtension("mkv"));
        Assert.True(template.MatchesExtension(".mp4"));
        Assert.False(template.MatchesExtension(".avi"));
        Assert.False(template.MatchesExtension(null));
        Assert.True((template with { Extensions = string.Empty }).MatchesExtension(null));
    }

    [Fact]
    public void HelpersReflectKindAndDisplayData()
    {
        var nfo = new TemplateInfo("default-nfo-movie", TemplateInfo.NfoKind, "movie", "en",
            TemplateInfo.DefaultSite, "1", string.Empty, string.Empty, "body");

        Assert.True(nfo.IsNfo);
        Assert.False((nfo with { Kind = TemplateInfo.DescriptionKind }).IsNfo);
        Assert.Equal("default-nfo-movie (movie, en)", nfo.DisplayName);
    }
}
