using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Text.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.Text.Models;

namespace SkyCD.Plugin.Metadata.Text.Tests;

public class TextMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsTheDocumentedAllowListOnly()
    {
        var descriptor = new TextMetadataPlugin().Descriptor;

        Assert.Equal(TextSchema.Extensions, descriptor.Extensions);
        Assert.Equal("skycd.text.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);

        Assert.Contains(".txt", descriptor.Extensions);
        Assert.Contains(".md", descriptor.Extensions);
        Assert.Contains(".csv", descriptor.Extensions);
        Assert.Contains(".cs", descriptor.Extensions);

        // Office documents are out of scope and must not be claimed.
        Assert.DoesNotContain(".docx", descriptor.Extensions);
        Assert.DoesNotContain(".xlsx", descriptor.Extensions);
        Assert.DoesNotContain(".pdf", descriptor.Extensions);
    }

    [Theory]
    [InlineData("utf-8-no-bom", "utf-8", false)]
    [InlineData("utf-8-bom", "utf-8", true)]
    [InlineData("utf-16le", "utf-16le", true)]
    [InlineData("utf-16be", "utf-16be", true)]
    [InlineData("latin1", "iso-8859-1", false)]
    public async Task Encoding_IsDetectedFromTheByteOrderMarkOrByValidation(
        string fixture, string expectedEncoding, bool expectedBom)
    {
        var result = await ExtractAsync(ForFixture(fixture), ".txt");

        Assert.Equal(expectedEncoding, Text(result, TextSchema.Encoding));
        Assert.Equal(expectedBom, Boolean(result, TextSchema.HasByteOrderMark));
    }

    [Fact]
    public async Task Utf16Source_IsNotMistakenForBinaryDespiteItsNullBytes()
    {
        // A byte-level control check would reject every UTF-16 file; detection runs on decoded characters.
        var result = await ExtractAsync(TextFixtures.Utf16Le(), ".txt");

        Assert.Equal("utf-16le", Text(result, TextSchema.Encoding));
        Assert.Contains("Second line", Text(result, TextSchema.TextExcerpt), StringComparison.Ordinal);
        Assert.Contains("☕", Text(result, TextSchema.TextExcerpt), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Latin1Fallback_DecodesRatherThanEmittingReplacementCharacters()
    {
        var result = await ExtractAsync(TextFixtures.Latin1("Café résumé"), ".txt");

        Assert.Equal("Café résumé", Text(result, TextSchema.TextExcerpt));
    }

    [Fact]
    public async Task BinaryContentIsRejected_RatherThanDisplayedAsArbitraryBytes()
    {
        await Assert.ThrowsAsync<BinaryContentRejectedException>(
            () => ExtractAsync(TextFixtures.BinaryMasqueradingAsText(), ".txt"));
    }

    [Fact]
    public async Task LineEndings_AreClassifiedIncludingMixedFiles()
    {
        Assert.Equal("lf", Text(await ExtractAsync(TextFixtures.WithLineEndings("\n", 3), ".txt"), TextSchema.LineEndings));
        Assert.Equal("crlf", Text(await ExtractAsync(TextFixtures.WithLineEndings("\r\n", 3), ".txt"), TextSchema.LineEndings));
        Assert.Equal("cr", Text(await ExtractAsync(TextFixtures.WithLineEndings("\r", 3), ".txt"), TextSchema.LineEndings));
        Assert.Equal("mixed", Text(await ExtractAsync(TextFixtures.MixedLineEndings(), ".txt"), TextSchema.LineEndings));
        Assert.Equal("none", Text(await ExtractAsync(TextFixtures.Utf8("single line"), ".txt"), TextSchema.LineEndings));
    }

    [Fact]
    public async Task LineCount_CountsATrailingUnterminatedLine()
    {
        Assert.Equal(3L, Integer(await ExtractAsync(TextFixtures.WithLineEndings("\n", 3), ".txt"), TextSchema.LineCount));
        Assert.Equal(3L, Integer(await ExtractAsync(TextFixtures.Utf8("a\nb\nc"), ".txt"), TextSchema.LineCount));
        Assert.Equal(1L, Integer(await ExtractAsync(TextFixtures.Utf8("single line"), ".txt"), TextSchema.LineCount));
        Assert.Equal(0L, Integer(await ExtractAsync(TextFixtures.Empty(), ".txt"), TextSchema.LineCount));
    }

    [Fact]
    public async Task HugeSingleLine_IsBoundedWithoutBufferingTheWholeLine()
    {
        var options = new TextPluginOptions(maxExcerptCharacters: 500);
        var text = TextFixtures.HugeSingleLine(200_000);

        var result = await new TextMetadataPlugin(options).ExtractAsync(
            new MediaItem(".log", "text/plain", text.Length), new ByteArrayMediaSource(text), null, CancellationToken.None);

        Assert.Equal(1L, Integer(result, TextSchema.LineCount));
        Assert.Equal(500, Text(result, TextSchema.TextExcerpt)!.Length);
        Assert.True(Boolean(result, TextSchema.ExcerptTruncated));
        Assert.Equal(200_000L, Integer(result, TextSchema.CharacterCount));
    }

    [Fact]
    public async Task ExcerptIsCutAtTheBound_AndTruncationIsReported()
    {
        var options = new TextPluginOptions(maxExcerptCharacters: 10);
        var result = await new TextMetadataPlugin(options).ExtractAsync(
            new MediaItem(".txt", "text/plain", TextFixtures.Utf8().Length),
            new ByteArrayMediaSource(TextFixtures.Utf8()),
            null,
            CancellationToken.None);

        Assert.Equal(10, Text(result, TextSchema.TextExcerpt)!.Length);
        Assert.True(Boolean(result, TextSchema.ExcerptTruncated));
    }

    [Fact]
    public async Task ScanWindow_IsBoundedAndReportsThatItIsIncomplete()
    {
        var options = new TextPluginOptions(maxScanBytes: 64);
        var text = TextFixtures.Utf8(new string('y', 4096));

        var source = new ByteArrayMediaSource(text);
        var result = await new TextMetadataPlugin(options).ExtractAsync(
            new MediaItem(".log", "text/plain", text.Length), source, null, CancellationToken.None);

        Assert.False(Boolean(result, TextSchema.ScanComplete));
        Assert.True(Boolean(result, TextSchema.ExcerptTruncated));

        // The byte length is the whole file, while the character count describes only what was scanned.
        Assert.Equal(text.Length, Integer(result, TextSchema.ByteLength));
        Assert.Equal(64L, Integer(result, TextSchema.CharacterCount));
        Assert.Equal(64, source.ChargedBytes);
    }

    [Fact]
    public async Task CompleteScan_IsReportedAsSuch()
    {
        var text = TextFixtures.Utf8();
        var result = await ExtractAsync(text, ".txt");

        Assert.True(Boolean(result, TextSchema.ScanComplete));
        Assert.False(Boolean(result, TextSchema.ExcerptTruncated));
        Assert.Equal(text.Length, Integer(result, TextSchema.ByteLength));
    }

    [Fact]
    public async Task AWindowCutInsideAMultibyteCharacter_StillDecodesAsUtf8()
    {
        // 64 bytes lands inside the three-byte sequence for the coffee emoji.
        var options = new TextPluginOptions(maxScanBytes: 64);
        var text = TextFixtures.Utf8(new string('a', 62) + "☕ trailing");

        var result = await new TextMetadataPlugin(options).ExtractAsync(
            new MediaItem(".txt", "text/plain", text.Length),
            new ByteArrayMediaSource(text),
            null,
            CancellationToken.None);

        Assert.Equal("utf-8", Text(result, TextSchema.Encoding));
        Assert.DoesNotContain("\uFFFD", Text(result, TextSchema.TextExcerpt), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MarkdownTitle_IsReadFromTheFirstHeading()
    {
        var result = await ExtractAsync(TextFixtures.Markdown("Release Notes"), ".md");

        Assert.Equal("Release Notes", Text(result, TextSchema.Title));
        Assert.Equal("markdown", Text(result, TextSchema.TextKind));
    }

    [Fact]
    public async Task NonMarkdownSources_DoNotGetAnInferredTitle()
    {
        var result = await ExtractAsync(TextFixtures.Utf8("# not a heading context"), ".txt");

        Assert.Null(Text(result, TextSchema.Title));
        Assert.Equal("text", Text(result, TextSchema.TextKind));
    }

    [Theory]
    [InlineData(".csv", "csv")]
    [InlineData(".cs", "source")]
    [InlineData(".json", "structured")]
    [InlineData(".log", "text")]
    public async Task TextKind_ComesFromTheAllowListCategory(string extension, string expectedKind)
    {
        var result = await ExtractAsync(TextFixtures.Utf8("content"), extension);

        Assert.Equal(expectedKind, Text(result, TextSchema.TextKind));
    }

    [Fact]
    public async Task HtmlSource_IsCataloguedAsInertTextAndNeverInterpreted()
    {
        var result = await ExtractAsync(TextFixtures.Html(), ".html");

        var excerpt = Text(result, TextSchema.TextExcerpt)!;

        // The markup survives verbatim as characters: nothing is parsed, stripped, executed or resolved.
        Assert.Contains("<script>alert('x')</script>", excerpt, StringComparison.Ordinal);
        Assert.Contains("https://example.com", excerpt, StringComparison.Ordinal);
        Assert.Equal("source", Text(result, TextSchema.TextKind));
    }

    [Fact]
    public async Task EmptySource_ReportsEmptyFactsWithoutAnExcerpt()
    {
        var result = await ExtractAsync(TextFixtures.Empty(), ".txt");

        Assert.Equal(0L, Integer(result, TextSchema.LineCount));
        Assert.Equal(0L, Integer(result, TextSchema.CharacterCount));
        Assert.True(Boolean(result, TextSchema.ScanComplete));
        Assert.DoesNotContain(result.Values, value => value.Key == TextSchema.TextExcerpt);
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var text = TextFixtures.Utf8(new string('z', 4096));
        var source = new ByteArrayMediaSource(text);
        var plugin = new TextMetadataPlugin(new TextPluginOptions(maxSourceBytes: text.Length - 1));

        await Assert.ThrowsAsync<TextSourceTooLargeException>(() => plugin.ExtractAsync(
            new MediaItem(".txt", "text/plain", text.Length), source, null, CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var text = TextFixtures.Utf8();
        var source = new ByteArrayMediaSource(text);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TextMetadataPlugin().ExtractAsync(
            new MediaItem(".txt", "text/plain", text.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = TextSchema.Metadata;
        var result = await ExtractAsync(TextFixtures.Markdown(), ".md");

        Assert.NotEmpty(result.Values);
        foreach (var value in result.Values)
        {
            var field = schema.Fields.SingleOrDefault(candidate => candidate.Key == value.Key);
            Assert.NotNull(field);
            Assert.Equal(field!.Type, value.Type);
        }

        Assert.Equal(result.Values.Count,
            result.Values.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Extraction_LeavesTheSourceBytesUnchanged()
    {
        var text = TextFixtures.Utf8WithBom();
        var before = (byte[])text.Clone();

        await ExtractAsync(text, ".txt");

        Assert.Equal(before, text);
    }

    [Fact]
    public void Options_RejectValuesThatCannotBeHonoured()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPluginOptions(maxExcerptCharacters: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPluginOptions(maxScanBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPluginOptions(maxSourceBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPluginOptions(maxBinaryControlRatio: 1.5));
    }

    private static byte[] ForFixture(string fixture) => fixture switch
    {
        "utf-8-no-bom" => TextFixtures.Utf8(),
        "utf-8-bom" => TextFixtures.Utf8WithBom(),
        "utf-16le" => TextFixtures.Utf16Le(),
        "utf-16be" => TextFixtures.Utf16Be(),
        _ => TextFixtures.Latin1()
    };

    internal static Task<MediaExtractionResult> ExtractAsync(byte[] text, string extension) =>
        new TextMetadataPlugin().ExtractAsync(
            new MediaItem(extension, "text/plain", text.Length),
            new ByteArrayMediaSource(text),
            progress: null,
            CancellationToken.None);

    internal static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    internal static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    internal static bool? Boolean(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Boolean;
}
