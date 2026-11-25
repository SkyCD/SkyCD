using System;
using System.Collections.Generic;
using SkyCD.Plugin.Ai.GenDesc.Exceptions;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.Ai.GenDesc.Parsing;
using Xunit;

namespace SkyCD.Plugin.Ai.GenDesc.Tests;

public class TemplateEngineTests
{
    private static DescriptionFacts Facts(params (string Key, string Value)[] pairs)
    {
        var facts = new DescriptionFacts();
        foreach (var (key, value) in pairs) facts.Force(key, value);
        return facts;
    }

    [Fact]
    public void SubstitutesVariableText()
    {
        Assert.Equal("Hello World!", TemplateEngine.Render("Hello <% Name %>!", Facts(("Name", "World"))));
    }

    [Fact]
    public void UnsetVariableRendersEmpty()
    {
        Assert.Equal("[]", TemplateEngine.Render("[<% Missing %>]", Facts()));
    }

    [Fact]
    public void IfOutputsWhenTruthyAndSkipsWhenFalsy()
    {
        Assert.Equal("ABC", TemplateEngine.Render("A<% if X %>B<% end %>C", Facts(("X", "1"))));
        Assert.Equal("AC", TemplateEngine.Render("A<% if X %>B<% end %>C", Facts()));
    }

    [Theory]
    [InlineData("0", "off")]
    [InlineData("", "off")]
    [InlineData("   ", "off")]
    [InlineData("yes", "on")]
    [InlineData("1", "on")]
    public void TruthinessTreatsEmptyAndZeroAsUnset(string value, string expected)
    {
        Assert.Equal(expected, TemplateEngine.Render("<% if Flag %>on<% else %>off<% end %>", Facts(("Flag", value))));
    }

    [Fact]
    public void ElseifChainPicksFirstMatch()
    {
        var body = "<% if A %>1<% elseif B %>2<% else %>3<% end %>";
        Assert.Equal("2", TemplateEngine.Render(body, Facts(("B", "set"))));
        Assert.Equal("1", TemplateEngine.Render(body, Facts(("A", "set"), ("B", "set"))));
        Assert.Equal("3", TemplateEngine.Render(body, Facts()));
    }

    [Fact]
    public void IsSetChecksKeyPresence()
    {
        Assert.Equal("yes", TemplateEngine.Render("<% if IsSet(\"Title\") %>yes<% end %>", Facts(("Title", "X"))));
        Assert.Equal(string.Empty, TemplateEngine.Render("<% if IsSet(\"Title\") %>yes<% end %>", Facts()));
    }

    [Fact]
    public void DefaultFallsBackToSecondArgument()
    {
        Assert.Equal("Film", TemplateEngine.Render("<% Default(\"Title\", Name) %>", Facts(("Name", "Film"))));
        Assert.Equal("Real", TemplateEngine.Render("<% Default(\"Title\", Name) %>", Facts(("Name", "Film"), ("Title", "Real"))));
    }

    [Fact]
    public void JoinSplitsFactOnPipes()
    {
        Assert.Equal("Action, Sci-Fi", TemplateEngine.Render("<% Join(\"Genre\", \", \") %>", Facts(("Genre", "Action|Sci-Fi"))));
    }

    [Fact]
    public void CountReturnsListItemNumber()
    {
        Assert.Equal("3", TemplateEngine.Render("<% count(\"Genre\") %>", Facts(("Genre", "A|B|C"))));
    }

    [Fact]
    public void BbcodeHelpersWrapNonEmptyOnly()
    {
        Assert.Equal("[b]Title[/b]", TemplateEngine.Render("<% b(Title) %>", Facts(("Title", "Title"))));
        Assert.Equal(string.Empty, TemplateEngine.Render("<% b(Title) %>", Facts()));
        Assert.Equal("[url=http://x]name[/url]", TemplateEngine.Render("<% url(\"name\", \"http://x\") %>", Facts()));
    }

    [Fact]
    public void NumberAndTextArithmeticFollowsOperandKinds()
    {
        Assert.Equal("5", TemplateEngine.Render("<% 2 + 3 %>", Facts()));
        Assert.Equal("121", TemplateEngine.Render("<% \"12\" + 1 %>", Facts()));
    }

    [Fact]
    public void NumericVariableComparesTextually()
    {
        Assert.Equal("Y", TemplateEngine.Render("<% if Year == 1999 %>Y<% end %>", Facts(("Year", "1999"))));
    }

    [Fact]
    public void TextFunctionsTransformValues()
    {
        Assert.Equal("FILM", TemplateEngine.Render("<% upper(Name) %>", Facts(("Name", "film"))));
        Assert.Equal("3", TemplateEngine.Render("<% len(Name) %>", Facts(("Name", "abc"))));
        Assert.Equal("ab", TemplateEngine.Render("<% cut(Name, 2) %>", Facts(("Name", "abcdef"))));
    }

    [Fact]
    public void NewlineHelpersPreserveInteriorLines()
    {
        Assert.Equal("x\ny", TemplateEngine.Render("<% tWriteLine(\"x\") %>y", Facts()));
    }

    [Fact]
    public void WhitespaceControlRemovesDirectiveLines()
    {
        Assert.Equal("ab", TemplateEngine.Render("a\n<%- if false -%>\nIGNORED\n<%- end -%>\nb", Facts()));
    }

    [Fact]
    public void NormalizeCollapsesBlankLineRuns()
    {
        Assert.Equal("L1\n\nL2", TemplateEngine.Render("L1\n\n\n\nL2", Facts()));
    }

    [Fact]
    public void MultipleBlocksRenderInOrder()
    {
        var rendered = TemplateEngine.Render("<% b(Name) %>|<% if Year %>(<% Year %>)<% end %>", Facts(("Name", "F"), ("Year", "2001")));
        Assert.Equal("[b]F[/b]|(2001)", rendered);
    }

    [Fact]
    public void CompileCountsTopLevelNodes()
    {
        Assert.Equal(2, TemplateEngine.Compile("before<% if X %>inside<% end %>").NodeCount);
    }

    [Fact]
    public void UnclosedIfThrows()
    {
        Assert.Throws<TemplateSyntaxException>(() => TemplateEngine.Compile("<% if A %>body"));
    }

    [Fact]
    public void StrayEndThrows()
    {
        Assert.Throws<TemplateSyntaxException>(() => TemplateEngine.Compile("x<% 1 %>y<% end %>"));
    }

    [Fact]
    public void UnterminatedBlockThrows()
    {
        Assert.Throws<TemplateSyntaxException>(() => TemplateEngine.Compile("<% Title "));
    }

    [Fact]
    public void UnknownFunctionThrows()
    {
        Assert.Throws<TemplateSyntaxException>(() => TemplateEngine.Compile("<% frobnicate(Title) %>"));
    }

    [Fact]
    public void UnterminatedStringThrows()
    {
        Assert.Throws<TemplateSyntaxException>(() => TemplateEngine.Compile("<% 'oops %>"));
    }
}
