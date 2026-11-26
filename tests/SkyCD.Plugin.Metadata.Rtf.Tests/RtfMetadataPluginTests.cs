using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Rtf.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.Rtf.Models;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.Rtf.Tests;

public class RtfMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsOnlyRtfAndAVersionedSchema()
    {
        var descriptor = new RtfMetadataPlugin().Descriptor;

        Assert.Equal([".rtf"], descriptor.Extensions);
        Assert.Equal(["application/rtf", "text/rtf"], descriptor.ContentTypes);
        Assert.Equal("skycd.rtf.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task HeaderAndInformationGroup_ArePublishedFieldByField()
    {
        var created = new DateTimeOffset(2021, 3, 4, 12, 34, 56, TimeSpan.Zero);
        var modified = new DateTimeOffset(2022, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var printed = new DateTimeOffset(2023, 7, 8, 9, 10, 11, TimeSpan.Zero);
        var rtf = RtfFixtures.Document(
            title: "Quarterly Report",
            subject: "Figures",
            author: "A. Writer",
            manager: "M. Manager",
            company: "Acme",
            operatorName: "O. Operator",
            category: "Finance",
            keywords: "finance; report",
            comment: "Reviewed",
            createdAt: created,
            modifiedAt: modified,
            printedAt: printed,
            revisionNumber: 7,
            editingTimeMinutes: 42,
            pageCount: 3,
            wordCount: 100,
            characterCount: 500,
            characterCountWithSpaces: 600,
            body: "Body text");

        var result = await ExtractAsync(rtf);

        Assert.Equal("rtf", Text(result, RtfSchema.Container));
        Assert.Equal(1L, Integer(result, RtfSchema.RtfVersion));
        Assert.Equal(1252L, Integer(result, RtfSchema.AnsiCodePage));
        Assert.False(Boolean(result, RtfSchema.IsProtected));
        Assert.Equal("Quarterly Report", Text(result, RtfSchema.Title));
        Assert.Equal("Figures", Text(result, RtfSchema.Subject));
        Assert.Equal("A. Writer", Text(result, RtfSchema.Author));
        Assert.Equal("M. Manager", Text(result, RtfSchema.Manager));
        Assert.Equal("Acme", Text(result, RtfSchema.Company));
        Assert.Equal("O. Operator", Text(result, RtfSchema.Operator));
        Assert.Equal("Finance", Text(result, RtfSchema.Category));
        Assert.Equal("finance; report", Text(result, RtfSchema.Keywords));
        Assert.Equal("Reviewed", Text(result, RtfSchema.Comment));
        Assert.Equal(7L, Integer(result, RtfSchema.RevisionNumber));
        Assert.Equal(42L, Integer(result, RtfSchema.EditingTimeMinutes));
        Assert.Equal(3L, Integer(result, RtfSchema.PageCount));
        Assert.Equal(100L, Integer(result, RtfSchema.WordCount));
        Assert.Equal(500L, Integer(result, RtfSchema.CharacterCount));
        Assert.Equal(600L, Integer(result, RtfSchema.CharacterCountWithSpaces));
        Assert.Equal(1L, Integer(result, RtfSchema.ParagraphCount));
        Assert.Equal(created, Timestamp(result, RtfSchema.CreatedAt));
        Assert.Equal(modified, Timestamp(result, RtfSchema.ModifiedAt));
        Assert.Equal(printed, Timestamp(result, RtfSchema.LastPrintedAt));
        Assert.Contains("Body text", Text(result, RtfSchema.TextExcerpt), StringComparison.Ordinal);
        Assert.False(Boolean(result, RtfSchema.TextExcerptTruncated));
    }

    [Fact]
    public async Task MissingInformationGroup_OmitsThoseFields()
    {
        var result = await ExtractAsync(RtfFixtures.Document(includeInfo: false));

        Assert.Equal("rtf", Text(result, RtfSchema.Container));
        Assert.Null(Text(result, RtfSchema.Title));
        Assert.Null(Text(result, RtfSchema.Author));
        Assert.DoesNotContain(result.Values, value => value.Key == RtfSchema.CreatedAt);
        Assert.DoesNotContain(result.Values, value => value.Key == RtfSchema.PageCount);
        Assert.Equal(1L, Integer(result, RtfSchema.ParagraphCount));
    }

    [Fact]
    public async Task EscapedAnsiAndUnicodeText_IsDecoded()
    {
        var rtf = RtfFixtures.Document(
            title: @"caf\'e9 \ldblquote Q\rdblquote",
            body: @"\u8212? Body");

        var result = await ExtractAsync(rtf);

        Assert.Equal("café “Q”", Text(result, RtfSchema.Title));
        Assert.Contains("— Body", Text(result, RtfSchema.TextExcerpt), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VendorGroupsAndLegacyCodepage_DecodeLikeTheFormerReader()
    {
        // 0xE8 is c-caron in Windows-1250 but e-grave in Windows-1252, so the title only
        // matches when the declared code page is honored. The font table, stylesheet and color
        // table carry no catalog facts and must not leak into the excerpt.
        var rtf =
            @"{\rtf1\ansi\ansicpg1250\deff0{\fonttbl{\f0 Calibri;}}{\stylesheet{\s0 Normal;}}{\colortbl;\red0\green0\blue0;}" +
            (@"{\info{\title Caf" + (char)0xE8 + @"}{\nofwords 12}}" + @"\pard Body\par}");

        var result = await ExtractAsync(System.Text.Encoding.Latin1.GetBytes(rtf));

        Assert.Equal("rtf", Text(result, RtfSchema.Container));
        Assert.Equal(1L, Integer(result, RtfSchema.RtfVersion));
        Assert.Equal(1250L, Integer(result, RtfSchema.AnsiCodePage));
        Assert.Equal("Caf\u010D", Text(result, RtfSchema.Title));
        Assert.Equal(12L, Integer(result, RtfSchema.WordCount));
        Assert.Contains("Body", Text(result, RtfSchema.TextExcerpt), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProtectedDocument_IsFlaggedAndStillReadable()
    {
        var result = await ExtractAsync(RtfFixtures.Document(
            title: "Protected",
            protectedDocument: true,
            body: "Readable body"));

        Assert.True(Boolean(result, RtfSchema.IsProtected));
        Assert.Equal("Protected", Text(result, RtfSchema.Title));
        Assert.Contains("Readable body", Text(result, RtfSchema.TextExcerpt), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TruncatedDocument_StillYieldsReadableMetadata()
    {
        var result = await ExtractAsync(RtfFixtures.Truncated());

        Assert.Equal("rtf", Text(result, RtfSchema.Container));
        Assert.Equal(1L, Integer(result, RtfSchema.RtfVersion));
    }

    [Fact]
    public async Task TextExcerpt_IsCutAtTheBoundAndTruncationIsReported()
    {
        var rtf = RtfFixtures.LongText(500);
        var plugin = new RtfMetadataPlugin(new RtfPluginOptions(maxTextExcerptCharacters: 100));

        var result = await plugin.ExtractAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            new ByteArrayMediaSource(rtf),
            progress: null,
            CancellationToken.None);

        Assert.Equal(100, Text(result, RtfSchema.TextExcerpt)!.Length);
        Assert.True(Boolean(result, RtfSchema.TextExcerptTruncated));
    }

    [Fact]
    public async Task TextlessDocument_OmitsTheExcerptRatherThanEmittingAnEmptyString()
    {
        var result = await ExtractAsync(RtfFixtures.EmptyBody());

        Assert.Equal(0L, Integer(result, RtfSchema.ParagraphCount));
        Assert.DoesNotContain(result.Values, value => value.Key == RtfSchema.TextExcerpt);
        Assert.DoesNotContain(result.Values, value => value.Key == RtfSchema.TextExcerptTruncated);
    }

    [Fact]
    public async Task NonRtfSource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<RtfDecodeException>(() => ExtractAsync(RtfFixtures.NotRtf()));
    }

    [Fact]
    public async Task EmptySource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<RtfDecodeException>(() => ExtractAsync([]));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var rtf = RtfFixtures.Document();
        var source = new ByteArrayMediaSource(rtf);
        var plugin = new RtfMetadataPlugin(new RtfPluginOptions(maxSourceBytes: rtf.Length - 1));

        await Assert.ThrowsAsync<RtfSourceTooLargeException>(() => plugin.ExtractAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length), source, null, CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var rtf = RtfFixtures.Document();
        var source = new ByteArrayMediaSource(rtf);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RtfMetadataPlugin().ExtractAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = RtfSchema.Metadata;
        var result = await ExtractAsync(RtfFixtures.Document(
            title: "Title",
            author: "Author",
            createdAt: new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero)));

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
        var rtf = RtfFixtures.Document(title: "Immutability");
        var before = (byte[])rtf.Clone();

        await ExtractAsync(rtf);

        Assert.Equal(before, rtf);
    }

    [Fact]
    public async Task ReadingChargesTheSourceExactlyOnce()
    {
        var rtf = RtfFixtures.Document(title: "Bounded");
        var source = new ByteArrayMediaSource(rtf);

        await new RtfMetadataPlugin().ExtractAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length), source, null, CancellationToken.None);

        Assert.Equal(rtf.Length, source.ChargedBytes);
        Assert.True(source.ReadCount < 16, $"A {rtf.Length} byte document took {source.ReadCount} host reads.");
    }

    internal static Task<MediaExtractionResult> ExtractAsync(byte[] rtf) =>
        new RtfMetadataPlugin().ExtractAsync(
            new MediaItem(".rtf", "application/rtf", rtf.Length),
            new ByteArrayMediaSource(rtf),
            progress: null,
            CancellationToken.None);

    internal static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    internal static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    internal static bool? Boolean(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Boolean;

    internal static DateTimeOffset? Timestamp(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Timestamp;
}
