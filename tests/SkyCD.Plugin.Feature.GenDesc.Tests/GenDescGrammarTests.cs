using System.IO;
using System.Linq;
using SkyCD.Plugin.Feature.GenDesc.Views;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class GenDescGrammarTests
{
    private static IGrammar LoadGrammar()
    {
        var path = GenDescTemplateHighlighting.EnsureGrammarFile();
        var registry = new Registry(new RegistryOptions(ThemeName.DarkPlus));
        return registry.LoadGrammarFromPathSync(path, 0, new System.Collections.Generic.Dictionary<string, int>());
    }

    private static string[] ScopesFor(IGrammar grammar, string line, string text)
    {
        var result = grammar.TokenizeLine(line);
        var token = result.Tokens.First(candidate =>
            line.Substring(candidate.StartIndex, candidate.Length) == text);
        return token.Scopes.ToArray();
    }

    [Fact]
    public void GrammarFileIsEmbedded()
    {
        var path = GenDescTemplateHighlighting.EnsureGrammarFile();

        Assert.True(File.Exists(path));
        Assert.Equal("source.gendesc", LoadGrammar().GetScopeName());
    }

    [Fact]
    public void ConditionalKeywordAndHelperAreScoped()
    {
        var grammar = LoadGrammar();
        const string line = "{{ if has(\"Year\") }}";

        Assert.Contains("keyword.control.gendesc", ScopesFor(grammar, line, "if"));
        Assert.Contains("entity.name.function.gendesc", ScopesFor(grammar, line, "has"));
        Assert.Contains("string.quoted.gendesc", ScopesFor(grammar, line, "\"Year\""));
    }

    [Fact]
    public void PipeTargetIsAFunction()
    {
        var grammar = LoadGrammar();

        Assert.Contains("entity.name.function.gendesc", ScopesFor(grammar, "{{ Title | upper }}", "upper"));
    }

    [Fact]
    public void BbCodeTagsAreScoped()
    {
        var grammar = LoadGrammar();

        Assert.Contains("entity.name.tag.gendesc", ScopesFor(grammar, "[b]Title[/b]", "[b]"));
    }

    [Fact]
    public void CommentBlockIsScoped()
    {
        var grammar = LoadGrammar();
        var result = grammar.TokenizeLine("{{# hidden }}");

        Assert.Contains(result.Tokens, token => token.Scopes.Contains("comment.line.gendesc"));
    }
}
