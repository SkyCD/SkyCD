using System;
using SkyCD.Plugin.Feature.GenDesc.Exceptions;
using SkyCD.Plugin.Feature.GenDesc.Parsing;
using SkyCD.Plugin.OnlineMetadata.Models;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class TemplateEngineTests
{
    private static DescriptionFacts Facts(params (string Key, string Value)[] pairs)
    {
        var facts = new DescriptionFacts();
        foreach (var pair in pairs)
        {
            facts.Force(pair.Key, pair.Value);
        }

        return facts;
    }

    [Fact]
    public void PlainTextIsCopiedVerbatim()
    {
        Assert.Equal("just text", TemplateEngine.Render("just text", Facts()));
    }

    [Fact]
    public void OutputsVariablesByNameCaseInsensitively()
    {
        Assert.Equal("Hello World!", TemplateEngine.Render("Hello <% name %>!", Facts(("Name", "World"))));
    }

    [Fact]
    public void MissingVariableOutputsNothing()
    {
        Assert.Equal("[]", TemplateEngine.Render("[<% Missing %>]", Facts()));
    }

    [Fact]
    public void ElseIfChainPicksTheFirstMatch()
    {
        const string body = "<% if Year == \"2024\" %>next<% elseif Year == \"2023\" %>last<% else %>other<% end %>";

        Assert.Equal("next", TemplateEngine.Render(body, Facts(("Year", "2024"))));
        Assert.Equal("last", TemplateEngine.Render(body, Facts(("Year", "2023"))));
        Assert.Equal("other", TemplateEngine.Render(body, Facts(("Year", "1999"))));
    }

    [Fact]
    public void FunctionsLookUpFactsAndJoinLists()
    {
        const string body = "<% Has(\"Year\") %>|<% Join(\"Genre\", \", \") %>|<% Default(\"Title\", Name) %>";

        Assert.Equal("true|Action, Drama|Example Movie",
            TemplateEngine.Render(body, Facts(("Year", "2024"), ("Genre", "Action|Drama"), ("Title", "Example Movie"))));
    }

    [Fact]
    public void FiltersChainLeftToRight()
    {
        Assert.Equal("EXAMPLE", TemplateEngine.Render("<% Title | upper %>", Facts(("Title", "Example"))));
        Assert.Equal("M0vie", TemplateEngine.Render("<% Name | replace(\"o\", \"0\") %>", Facts(("Name", "Movie"))));
        Assert.Equal("example-movie", TemplateEngine.Render("<% Title | slug %>", Facts(("Title", "Example Movie"))));
    }

    [Fact]
    public void WhitespaceControlTrimsAroundTags()
    {
        Assert.Equal("abc", TemplateEngine.Render("a\n<%- if Has(\"X\") -%>\nb\n<%- end -%>\nc",
            Facts(("X", "1"))));
        Assert.Equal("ac", TemplateEngine.Render("a<%- # note -%>c", Facts()));
    }

    [Fact]
    public void CommentsRenderNothing()
    {
        Assert.Equal("ab", TemplateEngine.Render("a<% # hidden %>b", Facts()));
    }

    [Fact]
    public void ConsecutiveBlankLinesCollapseAndEdgesAreTrimmed()
    {
        Assert.Equal("One\n\nTwo", TemplateEngine.Render("\nOne\n\n\nTwo\n\n", Facts()));
    }

    [Fact]
    public void BbCodeHelpersWrapExistingText()
    {
        Assert.Equal("[b]Title[/b] [i]Year[/i]", TemplateEngine.Render("<% b(\"Title\") %> <% i(\"Year\") %>", Facts()));
    }

    [Fact]
    public void UnknownFunctionFailsToCompile()
    {
        var exception = Assert.Throws<TemplateSyntaxException>(() =>
            TemplateEngine.Render("<% nope(\"x\") %>", Facts()));
        Assert.Contains("unknown template function", exception.Message);
    }

    [Fact]
    public void UnknownFilterFailsToCompile()
    {
        var exception = Assert.Throws<TemplateSyntaxException>(() =>
            TemplateEngine.Render("<% Name | nope %>", Facts()));
        Assert.Contains("unknown template filter", exception.Message);
    }

    [Fact]
    public void UnclosedConditionalFailsToCompile()
    {
        var exception = Assert.Throws<TemplateSyntaxException>(() =>
            TemplateEngine.Render("<% if Year %>x", Facts(("Year", "2024"))));
        Assert.Contains("unclosed", exception.Message);
    }

    [Fact]
    public void UnterminatedBlockFailsToCompile()
    {
        var exception = Assert.Throws<TemplateSyntaxException>(() =>
            TemplateEngine.Render("<% Year", Facts(("Year", "2024"))));
        Assert.Contains("unterminated", exception.Message);
    }

    [Fact]
    public void CompileReusesTheParsedTree()
    {
        var compiled = TemplateEngine.Compile("a<% Name %>b");

        Assert.Equal(3, compiled.NodeCount);
        Assert.Equal("aOneb", compiled.Render(Facts(("Name", "One"))));
        Assert.Equal("aTwob", compiled.Render(Facts(("Name", "Two"))));
    }
}
