using SkyCD.Plugin.Ai.GenDesc.Models;
using Xunit;

namespace SkyCD.Plugin.Ai.GenDesc.Tests;

public class TemplateInfoTests
{
    [Fact]
    public void ParseReadsKeyEqualsValueHeader()
    {
        var template = TemplateInfo.Parse("## id=my\n## site=imdb\n## lang=lt\n## version=7\nBody here", "fallback");

        Assert.Equal("my", template.Id);
        Assert.Equal("imdb", template.Site);
        Assert.Equal("lt", template.Language);
        Assert.Equal("7", template.Version);
        Assert.Equal("Body here", template.Body);
    }

    [Fact]
    public void ParseFallsBackWhenHeaderOmitsFields()
    {
        var template = TemplateInfo.Parse("no header at all", "fallback-id", "fr");

        Assert.Equal("fallback-id", template.Id);
        Assert.Equal(TemplateInfo.DefaultSite, template.Site);
        Assert.Equal("fr", template.Language);
        Assert.Equal("1", template.Version);
        Assert.Equal("no header at all", template.Body);
    }

    [Fact]
    public void ToFileContentRoundTripsThroughParse()
    {
        var original = new TemplateInfo("custom", "anidb", "lt", "3", "line one\nline two");
        var reparsed = TemplateInfo.Parse(original.ToFileContent(), "ignored", "en");

        Assert.Equal(original.Id, reparsed.Id);
        Assert.Equal(original.Site, reparsed.Site);
        Assert.Equal(original.Language, reparsed.Language);
        Assert.Equal(original.Version, reparsed.Version);
        Assert.Equal(original.Body, reparsed.Body);
    }

    [Fact]
    public void ParseLowercasesHeaderValues()
    {
        var template = TemplateInfo.Parse("## site=IMDb\n## lang=LT\nBody", "x");
        Assert.Equal("imdb", template.Site);
        Assert.Equal("lt", template.Language);
    }
}
