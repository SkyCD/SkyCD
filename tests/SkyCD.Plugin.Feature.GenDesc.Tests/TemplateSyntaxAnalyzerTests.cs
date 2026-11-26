using System.Linq;
using SkyCD.Plugin.Feature.GenDesc.Parsing;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class TemplateSyntaxAnalyzerTests
{
    private static TemplateSyntaxSpan[] Spans(string body) => TemplateSyntaxAnalyzer.Analyze(body).ToArray();

    [Fact]
    public void PlainTextProducesNoSpans()
    {
        Assert.Empty(Spans("plain text"));
    }

    [Fact]
    public void OutputTagIsSplitIntoTagsAndVariable()
    {
        var spans = Spans("{{ Name }}");

        Assert.Equal(
            [TemplateSyntaxKind.Tag, TemplateSyntaxKind.Variable, TemplateSyntaxKind.Tag],
            spans.Select(span => span.Kind).ToArray());
        Assert.Equal(0, spans[0].Start);
        Assert.Equal(2, spans[0].Length);
        Assert.Equal(3, spans[1].Start);
        Assert.Equal(4, spans[1].Length);
        Assert.Equal(8, spans[2].Start);
    }

    [Fact]
    public void ConditionalClassifiesKeywordFunctionAndString()
    {
        var spans = Spans("{{ if has(\"Year\") }}");

        Assert.Equal(
            [TemplateSyntaxKind.Tag, TemplateSyntaxKind.Keyword, TemplateSyntaxKind.Function,
                TemplateSyntaxKind.Operator, TemplateSyntaxKind.String, TemplateSyntaxKind.Operator,
                TemplateSyntaxKind.Tag],
            spans.Select(span => span.Kind).ToArray());
    }

    [Fact]
    public void ElseIfIsTwoKeywords()
    {
        var spans = Spans("{{ else if }}");

        Assert.Equal(
            [TemplateSyntaxKind.Tag, TemplateSyntaxKind.Keyword, TemplateSyntaxKind.Keyword,
                TemplateSyntaxKind.Tag],
            spans.Select(span => span.Kind).ToArray());
    }

    [Fact]
    public void FilterPipeIsAnOperator()
    {
        var spans = Spans("{{ Name | upper }}");

        Assert.Equal(
            [TemplateSyntaxKind.Tag, TemplateSyntaxKind.Variable, TemplateSyntaxKind.Operator,
                TemplateSyntaxKind.Variable, TemplateSyntaxKind.Tag],
            spans.Select(span => span.Kind).ToArray());
        Assert.Equal("|", "{{ Name | upper }}".Substring(spans[2].Start, spans[2].Length));
    }

    [Fact]
    public void NumbersAreRecognized()
    {
        var spans = Spans("{{ 42 }}");

        var number = Assert.Single(spans, span => span.Kind == TemplateSyntaxKind.Number);
        Assert.Equal(3, number.Start);
        Assert.Equal(2, number.Length);
    }

    [Fact]
    public void CommentTagIsOneSpan()
    {
        var span = Assert.Single(Spans("{{# hidden }}"));

        Assert.Equal(TemplateSyntaxKind.Comment, span.Kind);
        Assert.Equal(0, span.Start);
        Assert.Equal(13, span.Length);
    }

    [Fact]
    public void BbCodeTagsAreMarked()
    {
        var spans = Spans("[b]Title[/b]");

        Assert.Equal(2, spans.Length);
        Assert.Equal(0, spans[0].Start);
        Assert.Equal(3, spans[0].Length);
        Assert.Equal(8, spans[1].Start);
        Assert.Equal(4, spans[1].Length);
    }

    [Fact]
    public void BracketedNumbersAreNotBbCode()
    {
        Assert.Empty(Spans("list[1]"));
    }

    [Fact]
    public void UnterminatedTagIsLeftAsText()
    {
        Assert.Empty(Spans("{{ if Year"));
    }
}
