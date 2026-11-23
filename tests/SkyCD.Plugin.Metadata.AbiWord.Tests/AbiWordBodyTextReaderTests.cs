using System;
using System.Linq;
using System.Threading;
using Xunit;
using SkyCD.Plugin.Metadata.AbiWord.Exceptions;
using SkyCD.Plugin.Metadata.AbiWord.Parsing;

namespace SkyCD.Plugin.Metadata.AbiWord.Tests;

public class AbiWordBodyTextReaderTests
{
    [Fact]
    public void Paragraphs_AreJoinedByLineBreaks()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.Paragraph("First") + AbiWordFixtures.Paragraph("Second")));

        Assert.Equal("First\nSecond", Read(xml));
    }

    [Fact]
    public void RunsInsideAParagraph_AreConcatenated()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.RunParagraph("Hello ", "dear ", "reader")));

        Assert.Equal("Hello dear reader", Read(xml));
    }

    [Fact]
    public void SelfClosingParagraphs_ContributeNoLine()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.EmptyParagraph + AbiWordFixtures.Paragraph("Only") + AbiWordFixtures.EmptyParagraph));

        Assert.Equal("Only", Read(xml));
    }

    [Fact]
    public void WhitespaceOnlyParagraphs_AreDropped()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.Paragraph("   ") + AbiWordFixtures.Paragraph("Text")));

        Assert.Equal("Text", Read(xml));
    }

    [Fact]
    public void ImagePlaceholders_ContributeNoText()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.Paragraph("Before " + AbiWordFixtures.EmbeddedImage + " after")));

        Assert.Equal("Before  after", Read(xml));
    }

    [Fact]
    public void OversizedDocument_ReportsNoText()
    {
        var xml = AbiWordFixtures.Plain(AbiWordFixtures.Paragraph("Text"));

        Assert.Empty(AbiWordBodyTextReader.Read(xml, 100, 10, 16, CancellationToken.None));
    }

    [Fact]
    public void EntitiesAndCharacterReferences_AreDecoded()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.Paragraph("A &amp; B&#381;&#8203;")));

        Assert.Equal("A & B\u017D\u200B", Read(xml));
    }

    [Fact]
    public void UndeclaredNamedEntities_ReportNoText()
    {
        // An undeclared entity is a parse error, so the document contributes no text.
        var xml = AbiWordFixtures.Bytes(Fixture(AbiWordFixtures.Paragraph("A&nbsp;B")));

        Assert.Empty(Read(xml));
    }

    [Theory]
    [InlineData("quot", "\"")]
    [InlineData("apos", "'")]
    [InlineData("lt", "<")]
    [InlineData("gt", ">")]
    public void PredefinedEntities_AreDecoded(string entity, string expected)
    {
        var xml = AbiWordFixtures.Bytes(Fixture(AbiWordFixtures.Paragraph($"&{entity};")));

        Assert.Equal(expected, Read(xml));
    }

    [Fact]
    public void CustomEntities_AreNeverExpanded()
    {
        var xml = AbiWordFixtures.Bytes(
            "<!DOCTYPE abiword [<!ENTITY secret 'expanded'>]>" +
            "<abiword><section><p>&secret;</p><p>After</p></section></abiword>");

        Assert.Empty(Read(xml));
    }

    [Fact]
    public void TabledCells_AreSkipped()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.Paragraph("Body") +
            AbiWordFixtures.Table("cell") +
            AbiWordFixtures.Paragraph("Tail")));

        Assert.Equal("Body\nTail", Read(xml));
    }

    [Fact]
    public void CharacterCap_StopsCollection()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.Paragraph("First") + AbiWordFixtures.Paragraph("Second")));

        Assert.Equal("Fir", Read(xml, maxCharacters: 3));
    }

    [Fact]
    public void CharacterCap_TruncatesAnOversizedParagraph()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.Paragraph(new string('a', 5000))));

        Assert.Equal(new string('a', 100), Read(xml, maxCharacters: 100));
    }

    [Fact]
    public void ParagraphCap_StopsTheWalk()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.Paragraph("One") + AbiWordFixtures.Paragraph("Two") +
            AbiWordFixtures.Paragraph("Three")));

        Assert.Equal("One", Read(xml, maxParagraphs: 1));
    }

    [Fact]
    public void LongRunsInsideOneParagraph_AreBounded()
    {
        var xml = AbiWordFixtures.Bytes(Fixture(
            AbiWordFixtures.RunParagraph(string.Concat(Enumerable.Repeat("ab", 4000)))));

        Assert.Equal(50, Read(xml, maxCharacters: 50).Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not XML")]
    [InlineData("<document><section><p>x</p></section></document>")]
    [InlineData("<abiword xmlns='urn:other'><section><p>x</p></section></abiword>")]
    [InlineData("<abiword><section><p>unfinished")]
    [InlineData("<abiword><section><p>x</p></section></abiword><trailing/>")]
    [InlineData("<abiword><section><p><table><p>x</p></table></p></section></abiword>")]
    public void UnreadableOrForeignDocuments_ReportNoText(string xml)
    {
        Assert.Empty(Read(AbiWordFixtures.Bytes(xml)));
    }

    [Fact]
    public void MixedNamespaces_EndTheWalk()
    {
        var xml = AbiWordFixtures.Bytes(
            "<abiword><section><p>Kept</p><p xmlns='urn:other'>foreign</p></section></abiword>");

        Assert.Throws<AbiWordDecodeException>(() => Read(xml));
    }

    [Fact]
    public void DocumentsWithoutANamespace_AreRead()
    {
        var xml = AbiWordFixtures.Bytes("<abiword><section><p>Plain</p></section></abiword>");

        Assert.Equal("Plain", Read(xml));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveLimits_ReturnNothing(int maxCharacters)
    {
        var xml = AbiWordFixtures.Plain(AbiWordFixtures.Paragraph("Text"));

        Assert.Empty(AbiWordBodyTextReader.Read(xml, maxCharacters, 10, 1024 * 1024, CancellationToken.None));
    }

    [Fact]
    public void CancelledToken_StopsTheWalk()
    {
        var xml = AbiWordFixtures.Plain(AbiWordFixtures.Paragraph("Text"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            AbiWordBodyTextReader.Read(xml, 100, 10, 1024 * 1024, cancelled.Token));
    }

    private static string Fixture(string body) => AbiWordFixtures.Document(body, AbiWordFixtures.EmptyMetadata);

    private static string Read(byte[] xml, int maxCharacters = 1000, int maxParagraphs = 100) =>
        AbiWordBodyTextReader.Read(xml, maxCharacters, maxParagraphs, 32 * 1024 * 1024, CancellationToken.None);
}
