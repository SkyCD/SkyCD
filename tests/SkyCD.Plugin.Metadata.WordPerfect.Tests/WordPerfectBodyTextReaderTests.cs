using System;
using Xunit;
using SkyCD.Plugin.Metadata.WordPerfect.Parsing;

namespace SkyCD.Plugin.Metadata.WordPerfect.Tests;

public class WordPerfectBodyTextReaderTests
{
    [Fact]
    public void PlainTextAndParagraphBreaks_AreRecovered()
    {
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("First"),
            [0xC7],
            WordPerfectFixtures.Text("Second"),
            [0xCC]);

        Assert.Equal("First\nSecond\n", WordPerfectBodyTextReader.Read(body, 0, 1000));
    }

    [Fact]
    public void MultinationalBytes_AreDecoded()
    {
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("M"),
            [0x1A],
            WordPerfectFixtures.Text("ller"));

        Assert.Equal("M\u00F6ller", WordPerfectBodyTextReader.Read(body, 0, 1000));
    }

    [Fact]
    public void SpacesAndHyphens_ArePreserved()
    {
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("A"),
            [0x80, 0x81, 0x84, 0x85],
            WordPerfectFixtures.Text("B"));

        Assert.Equal("A  --B", WordPerfectBodyTextReader.Read(body, 0, 1000));
    }

    [Fact]
    public void VariableGroup_IsSkippedByItsDeclaredSize()
    {
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("A"),
            WordPerfectFixtures.VariableGroup(0xD3, 0, WordPerfectFixtures.Text("hidden")),
            WordPerfectFixtures.Text("B"));

        Assert.Equal("AB", WordPerfectBodyTextReader.Read(body, 0, 1000));
    }

    [Fact]
    public void FixedGroup_IsSkippedByItsTableSize()
    {
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("A"),
            WordPerfectFixtures.FixedGroup(0xF2, 12),
            WordPerfectFixtures.Text("B"));

        Assert.Equal("AB", WordPerfectBodyTextReader.Read(body, 0, 1000));
    }

    [Fact]
    public void CorruptVariableSize_EndsTheWalkButKeepsEarlierText()
    {
        var group = WordPerfectFixtures.VariableGroup(0xD3, 0, WordPerfectFixtures.Text("hidden"));
        group[2] = 0xFF;
        group[3] = 0xFF;
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("Before"),
            group,
            WordPerfectFixtures.Text("After"));

        Assert.Equal("Before", WordPerfectBodyTextReader.Read(body, 0, 1000));
    }

    [Fact]
    public void FixedGroupWithoutItsClosingId_EndsTheWalk()
    {
        var group = WordPerfectFixtures.FixedGroup(0xF2, 12);
        group[^1] = 0xF3;
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("Before"),
            group,
            WordPerfectFixtures.Text("After"));

        Assert.Equal("Before", WordPerfectBodyTextReader.Read(body, 0, 1000));
    }

    [Fact]
    public void UnassignedToken_IsSkippedWithoutEndingTheWalk()
    {
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("A"),
            [0xFF],
            WordPerfectFixtures.Text("B"));

        Assert.Equal("AB", WordPerfectBodyTextReader.Read(body, 0, 1000));
    }

    [Fact]
    public void RepeatedLineBreaks_Collapse()
    {
        var body = WordPerfectFixtures.Body([0xC7, 0xC7, 0xC7]);

        Assert.Equal("\n", WordPerfectBodyTextReader.Read(body, 0, 1000));
    }

    [Fact]
    public void CharacterCap_StopsCollection()
    {
        var body = WordPerfectFixtures.Text("abcdef");

        Assert.Equal("abc", WordPerfectBodyTextReader.Read(body, 0, 3));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(100)]
    public void StartOutsideTheBody_ReturnsNothing(int start)
    {
        var body = WordPerfectFixtures.Text("abcdef");

        Assert.Empty(WordPerfectBodyTextReader.Read(body, start, 1000));
    }

    [Fact]
    public void TruncatedVariableGroup_EndsTheWalk()
    {
        var group = WordPerfectFixtures.VariableGroup(0xD3, 0, WordPerfectFixtures.Text("hidden"));
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("Kept"),
            group.AsSpan(0, group.Length - 2).ToArray());

        Assert.Equal("Kept", WordPerfectBodyTextReader.Read(body, 0, 1000));
    }
}
