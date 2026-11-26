using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Html.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.Html.Models;

namespace SkyCD.Plugin.Metadata.Html.Tests;

public class HtmlMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsHtmlAndAVersionedSchema()
    {
        var descriptor = new HtmlMetadataPlugin().Descriptor;

        Assert.Equal([".html", ".htm"], descriptor.Extensions);
        Assert.Equal(["text/html"], descriptor.ContentTypes);
        Assert.Equal("skycd.html.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task FullDocument_IsPublishedFieldByField()
    {
        var result = await ExtractAsync(HtmlFixtures.Full());

        Assert.Equal("html", Text(result, HtmlSchema.Container));
        Assert.Equal("Quarterly Report", Text(result, HtmlSchema.Title));
        Assert.Equal("Q3 figures", Text(result, HtmlSchema.Description));
        Assert.Equal("finance, report", Text(result, HtmlSchema.Keywords));
        Assert.Equal("Finance Team", Text(result, HtmlSchema.Author));
        Assert.Equal("SkyCD 3.0", Text(result, HtmlSchema.Generator));
        Assert.Equal("en-US", Text(result, HtmlSchema.Language));
        Assert.Equal("utf-8", Text(result, HtmlSchema.DeclaredCharset));
        Assert.True(Boolean(result, HtmlSchema.HasBaseHref));
        Assert.Equal(2L, Integer(result, HtmlSchema.LinkCount));
        Assert.Equal(2L, Integer(result, HtmlSchema.ImageCount));
        Assert.Equal(2L, Integer(result, HtmlSchema.ScriptCount));
        Assert.Equal("Quarterly\nBody text with a link and another.", Text(result, HtmlSchema.TextExcerpt));
        Assert.False(Boolean(result, HtmlSchema.TextExcerptTruncated));
        Assert.Equal(8L, Integer(result, HtmlSchema.WordCount));
        Assert.Equal(44L, Integer(result, HtmlSchema.CharacterCount));
    }

    [Fact]
    public async Task FragmentWithoutHead_OmitsHeadFieldsButCountsStructure()
    {
        var result = await ExtractAsync(HtmlFixtures.Fragment());

        Assert.DoesNotContain(result.Values, value => value.Key == HtmlSchema.Title);
        Assert.DoesNotContain(result.Values, value => value.Key == HtmlSchema.Description);
        Assert.DoesNotContain(result.Values, value => value.Key == HtmlSchema.Language);
        Assert.DoesNotContain(result.Values, value => value.Key == HtmlSchema.DeclaredCharset);
        Assert.False(Boolean(result, HtmlSchema.HasBaseHref));
        Assert.Equal(1L, Integer(result, HtmlSchema.LinkCount));
        Assert.Equal(0L, Integer(result, HtmlSchema.ImageCount));
        Assert.Equal(0L, Integer(result, HtmlSchema.ScriptCount));
        Assert.Equal("Just a fragment with no envelope.", Text(result, HtmlSchema.TextExcerpt));
        Assert.Equal(6L, Integer(result, HtmlSchema.WordCount));
    }

    [Fact]
    public async Task EmptyHead_OmitsHeadFields()
    {
        var result = await ExtractAsync(HtmlFixtures.EmptyHead());

        Assert.DoesNotContain(result.Values, value => value.Key == HtmlSchema.Title);
        Assert.DoesNotContain(result.Values, value => value.Key == HtmlSchema.Language);
        Assert.False(Boolean(result, HtmlSchema.HasBaseHref));
        Assert.Equal("Hi", Text(result, HtmlSchema.TextExcerpt));
        Assert.Equal(1L, Integer(result, HtmlSchema.WordCount));
        Assert.Equal(2L, Integer(result, HtmlSchema.CharacterCount));
    }

    [Fact]
    public async Task Utf8Document_PreservesNonAsciiText()
    {
        var result = await ExtractAsync(HtmlFixtures.Utf8());

        Assert.Equal("H\u00e9llo", Text(result, HtmlSchema.Title));
        Assert.Equal("Caf\u00e9 na\u00efve", Text(result, HtmlSchema.TextExcerpt));
    }

    [Fact]
    public async Task LegacyCharsetDocument_DecodesAndReportsItsDeclaredCharset()
    {
        var result = await ExtractAsync(HtmlFixtures.LegacyCharset());

        Assert.Equal("Caf\u00e9 prices", Text(result, HtmlSchema.Title));
        Assert.Equal("windows-1252", Text(result, HtmlSchema.DeclaredCharset));
        Assert.Contains("na\u00efve rates \u20ac", Text(result, HtmlSchema.TextExcerpt), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MetadataValues_AreBounded()
    {
        var plugin = new HtmlMetadataPlugin(new HtmlPluginOptions(maxMetadataValueCharacters: 5));

        var result = await plugin.ExtractAsync(
            new MediaItem(".html", "text/html", 100),
            new ByteArrayMediaSource(HtmlFixtures.Full()),
            progress: null,
            CancellationToken.None);

        Assert.Equal("Quart", Text(result, HtmlSchema.Title));
    }

    [Fact]
    public async Task TextExcerpt_IsCutAtTheBoundAndTruncationIsReported()
    {
        var plugin = new HtmlMetadataPlugin(new HtmlPluginOptions(maxTextExcerptCharacters: 10));
        var source = HtmlFixtures.Full();

        var result = await plugin.ExtractAsync(
            new MediaItem(".html", "text/html", source.Length),
            new ByteArrayMediaSource(source),
            progress: null,
            CancellationToken.None);

        Assert.Equal("Quarterly\n", Text(result, HtmlSchema.TextExcerpt));
        Assert.True(Boolean(result, HtmlSchema.TextExcerptTruncated));
    }

    [Fact]
    public async Task ScriptOnlyDocument_PublishesZeroCountsAndNoExcerpt()
    {
        var source = HtmlFixtures.Text("<html><head><script>var x = 1;</script></head><body><script>y();</script></body></html>");
        var result = await ExtractAsync(source);

        Assert.Equal(0L, Integer(result, HtmlSchema.WordCount));
        Assert.Equal(0L, Integer(result, HtmlSchema.CharacterCount));
        Assert.Equal(0L, Integer(result, HtmlSchema.LinkCount));
        Assert.Equal(2L, Integer(result, HtmlSchema.ScriptCount));
        Assert.DoesNotContain(result.Values, value => value.Key == HtmlSchema.TextExcerpt);
        Assert.DoesNotContain(result.Values, value => value.Key == HtmlSchema.TextExcerptTruncated);
    }

    [Fact]
    public async Task BinarySource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<HtmlDecodeException>(() => ExtractAsync(HtmlFixtures.WithNullByte()));
    }

    [Fact]
    public async Task EmptySource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<HtmlDecodeException>(() => ExtractAsync([]));
    }

    [Fact]
    public async Task DeeplyNestedDocument_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<HtmlDecodeException>(() => ExtractAsync(HtmlFixtures.DeeplyNested()));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var source = HtmlFixtures.Full();
        var mediaSource = new ByteArrayMediaSource(source);
        var plugin = new HtmlMetadataPlugin(new HtmlPluginOptions(maxSourceBytes: source.Length - 1));

        await Assert.ThrowsAsync<HtmlSourceTooLargeException>(() => plugin.ExtractAsync(
            new MediaItem(".html", "text/html", source.Length), mediaSource, null, CancellationToken.None));

        Assert.Equal(0, mediaSource.ChargedBytes);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var source = HtmlFixtures.Full();
        var mediaSource = new ByteArrayMediaSource(source);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HtmlMetadataPlugin().ExtractAsync(
            new MediaItem(".html", "text/html", source.Length), mediaSource, null, cancelled.Token));

        Assert.Equal(0, mediaSource.ChargedBytes);
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = HtmlSchema.Metadata;
        var result = await ExtractAsync(HtmlFixtures.Full());

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
        var source = HtmlFixtures.Full();
        var before = (byte[])source.Clone();

        await ExtractAsync(source);

        Assert.Equal(before, source);
    }

    [Fact]
    public async Task ReadingChargesTheSourceExactlyOnce()
    {
        var source = HtmlFixtures.Full();
        var mediaSource = new ByteArrayMediaSource(source);

        await new HtmlMetadataPlugin().ExtractAsync(
            new MediaItem(".html", "text/html", source.Length), mediaSource, null, CancellationToken.None);

        Assert.Equal(source.Length, mediaSource.ChargedBytes);
        Assert.True(mediaSource.ReadCount < 16,
            $"A {source.Length} byte document took {mediaSource.ReadCount} host reads.");
    }

    private static Task<MediaExtractionResult> ExtractAsync(byte[] source) =>
        new HtmlMetadataPlugin().ExtractAsync(
            new MediaItem(".html", "text/html", source.Length),
            new ByteArrayMediaSource(source),
            progress: null,
            CancellationToken.None);

    private static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    private static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    private static bool? Boolean(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Boolean;
}
