using System;
using System.Threading;
using Xunit;
using SkyCD.Plugin.Metadata.WordPerfect.Exceptions;
using SkyCD.Plugin.Metadata.WordPerfect.Parsing;

namespace SkyCD.Plugin.Metadata.WordPerfect.Tests;

public class WordPerfectBodyTextImporterTests
{
    [Fact]
    public void StructuredParagraphs_AreRecovered()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Body(
            WordPerfectFixtures.Wp6Text("First paragraph"),
            [0xCA],
            WordPerfectFixtures.Wp6Text("Second paragraph")));

        Assert.Equal("First paragraph\nSecond paragraph", Read(document));
    }

    [Fact]
    public void WordPerfectFiveBody_IsRecovered()
    {
        var document = WordPerfectFixtures.Wp5DocumentWithBody(
            WordPerfectFixtures.Text("WP5 first line\nSecond line"));

        Assert.Equal("WP5 first line\nSecond line", Read(document));
    }

    [Fact]
    public void MultinationalByte_IsDecoded()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Body(
            [0x4D, 0x1A, 0x6C, 0x6C, 0x65, 0x72]));

        Assert.Equal("M\u00F6ller", Read(document));
    }

    [Fact]
    public void SpacesAndHyphens_KeepTheirGraphicForm()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("A"),
            [0x80, 0x81, 0x84, 0x85],
            WordPerfectFixtures.Text("B")));

        Assert.Equal("A \u00A0-B", Read(document));
    }

    [Fact]
    public void VariableAndFixedGroups_DoNotContributeText()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("A"),
            WordPerfectFixtures.VariableGroup(0xD3, 0, WordPerfectFixtures.Text("hidden")),
            WordPerfectFixtures.FixedGroup(0xF2, 12),
            WordPerfectFixtures.Text("B")));

        Assert.Equal("AB", Read(document));
    }

    [Fact]
    public void CharacterCap_TruncatesTheRecoveredText()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("abcdef")));

        Assert.Equal("abc", Read(document, maxCharacters: 3));
    }

    [Fact]
    public void TextlessBody_ReturnsNothing()
    {
        var document = WordPerfectFixtures.DocumentWithBody([]);

        Assert.Null(Read(document));
    }

    [Fact]
    public void SalvageQuality_ReturnsNothingInsteadOfGuessedText()
    {
        var document = WordPerfectFixtures.WithSummary(
            [], fileType: 0x2C, body: WordPerfectFixtures.Text("Guessed body"));

        Assert.Null(Read(document));
    }

    [Fact]
    public void CorruptVariableGroup_FailsAsADecodeError()
    {
        var corrupt = WordPerfectFixtures.VariableGroup(0xD3, 0, WordPerfectFixtures.Text("hidden"));
        corrupt[2] = 0xFF;
        corrupt[3] = 0xFF;
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("Before"),
            corrupt));

        Assert.Throws<WordPerfectDecodeException>(() => Read(document));
    }

    [Fact]
    public void CancelledToken_StopsTheImport()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("Hello")));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            WordPerfectBodyTextImporter.ReadStructuredText(document, 1000, cancelled.Token));
    }

    private static string? Read(byte[] document, int maxCharacters = 1000) =>
        WordPerfectBodyTextImporter.ReadStructuredText(document, maxCharacters, CancellationToken.None);
}