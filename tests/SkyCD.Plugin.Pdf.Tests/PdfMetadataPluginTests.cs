using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Pdf.Exceptions;
using Xunit;
using SkyCD.Plugin.Pdf.Models;

namespace SkyCD.Plugin.Pdf.Tests;

public class PdfMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsOnlyPdfAndAVersionedSchema()
    {
        var descriptor = new PdfMetadataPlugin().Descriptor;

        Assert.Equal([".pdf"], descriptor.Extensions);
        Assert.Equal(["application/pdf"], descriptor.ContentTypes);
        Assert.Equal("skycd.pdf.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task SinglePageDocument_ReportsPageCountVersionAndGeometry()
    {
        var result = await ExtractAsync(PdfFixtures.Document());

        Assert.Equal("pdf", Text(result, PdfSchema.Container));
        Assert.Equal(1L, Integer(result, PdfSchema.PageCount));
        Assert.Equal(1.7, Number(result, PdfSchema.PdfVersion));
        Assert.Equal(PdfFixtures.DefaultPageWidth, Number(result, PdfSchema.PageWidth));
        Assert.Equal(PdfFixtures.DefaultPageHeight, Number(result, PdfSchema.PageHeight));
        Assert.False(Boolean(result, PdfSchema.IsEncrypted));
    }

    [Fact]
    public async Task MultiPageDocument_ReportsEveryPage()
    {
        var result = await ExtractAsync(PdfFixtures.Document(pageCount: 7));

        Assert.Equal(7L, Integer(result, PdfSchema.PageCount));
    }

    [Fact]
    public async Task InformationDictionary_IsPublishedFieldByField()
    {
        var created = new DateTimeOffset(2021, 3, 4, 12, 34, 56, TimeSpan.FromHours(2));
        var modified = new DateTimeOffset(2022, 5, 6, 7, 8, 9, TimeSpan.Zero);

        var result = await ExtractAsync(PdfFixtures.Document(
            title: "Quarterly Report",
            author: "A. Writer",
            subject: "Figures",
            keywords: "finance; report",
            creator: "SkyCD Writer",
            producer: "SkyCD Producer",
            createdAt: created,
            modifiedAt: modified));

        Assert.Equal("Quarterly Report", Text(result, PdfSchema.Title));
        Assert.Equal("A. Writer", Text(result, PdfSchema.Author));
        Assert.Equal("Figures", Text(result, PdfSchema.Subject));
        Assert.Equal("finance; report", Text(result, PdfSchema.Keywords));
        Assert.Equal("SkyCD Writer", Text(result, PdfSchema.Creator));
        Assert.Equal("SkyCD Producer", Text(result, PdfSchema.Producer));

        var createdValue = result.Values.Single(value => value.Key == PdfSchema.CreatedAt);
        Assert.Equal(MediaMetadataType.Timestamp, createdValue.Type);
        Assert.Equal(created, createdValue.Timestamp);

        var modifiedValue = result.Values.Single(value => value.Key == PdfSchema.ModifiedAt);
        Assert.Equal(modified, modifiedValue.Timestamp);
    }

    [Fact]
    public async Task DocumentWithoutAnInformationDictionary_OmitsThoseFields()
    {
        var result = await ExtractAsync(PdfFixtures.Document());

        Assert.Null(Text(result, PdfSchema.Title));
        Assert.Null(Text(result, PdfSchema.Author));
        Assert.DoesNotContain(result.Values, value => value.Key == PdfSchema.CreatedAt);
        Assert.Equal(1L, Integer(result, PdfSchema.PageCount));
    }

    [Fact]
    public async Task TextLayer_IsPublishedAsABoundedExcerpt()
    {
        var result = await ExtractAsync(PdfFixtures.Document(text: "Hello PDF"));

        Assert.Contains("Hello PDF", Text(result, PdfSchema.TextExcerpt), StringComparison.Ordinal);
        Assert.False(Boolean(result, PdfSchema.TextExcerptTruncated));
    }

    [Fact]
    public async Task ExcerptIsCutAtTheBound_AndTruncationIsReported()
    {
        var pdf = PdfFixtures.LongText(500);
        var plugin = new PdfMetadataPlugin(new PdfPluginOptions(maxTextExcerptCharacters: 100));

        var result = await plugin.ExtractAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length),
            new ByteArrayMediaSource(pdf),
            null,
            CancellationToken.None);

        Assert.Equal(100, Text(result, PdfSchema.TextExcerpt)!.Length);
        Assert.True(Boolean(result, PdfSchema.TextExcerptTruncated));
    }

    [Fact]
    public async Task TextlessDocument_OmitsTheExcerptRatherThanEmittingAnEmptyString()
    {
        var result = await ExtractAsync(PdfFixtures.Textless());

        Assert.Equal(2L, Integer(result, PdfSchema.PageCount));
        Assert.DoesNotContain(result.Values, value => value.Key == PdfSchema.TextExcerpt);
        Assert.DoesNotContain(result.Values, value => value.Key == PdfSchema.TextExcerptTruncated);
    }

    [Fact]
    public async Task EncryptedDocument_ReportsAnAuthRequiredStateAndStoresNoCredential()
    {
        var pdf = PdfFixtures.Document(encrypted: true);

        var exception = await Assert.ThrowsAsync<PdfAuthenticationRequiredException>(
            () => ExtractAsync(pdf));

        Assert.Contains("password protected", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("truncated")]
    [InlineData("notpdf")]
    public async Task UnreadableSources_FailWithADecodeError(string kind)
    {
        var pdf = kind switch
        {
            "malformed" => PdfFixtures.Malformed(),
            "truncated" => PdfFixtures.Truncated(PdfFixtures.Document(pageCount: 3)),
            _ => PdfFixtures.NotPdf()
        };

        await Assert.ThrowsAsync<PdfDecodeException>(() => ExtractAsync(pdf));
    }

    [Fact]
    public async Task EmptySource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<PdfDecodeException>(() => ExtractAsync([]));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var pdf = PdfFixtures.Document();
        var source = new ByteArrayMediaSource(pdf);
        var plugin = new PdfMetadataPlugin(new PdfPluginOptions(maxSourceBytes: pdf.Length - 1));

        await Assert.ThrowsAsync<PdfSourceTooLargeException>(() => plugin.ExtractAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length), source, null, CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var pdf = PdfFixtures.Document();
        var source = new ByteArrayMediaSource(pdf);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PdfMetadataPlugin().ExtractAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = PdfSchema.Metadata;
        var result = await ExtractAsync(PdfFixtures.Document(
            pageCount: 2,
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
        var pdf = PdfFixtures.Document(title: "Immutability", pageCount: 2);
        var before = (byte[])pdf.Clone();

        await ExtractAsync(pdf);

        Assert.Equal(before, pdf);
    }

    [Fact]
    public async Task ReadingNeverTouchesTheFilesystem_OnlyTheBoundedSource()
    {
        // The plugin is handed bytes, not a path, so a document referencing an external file cannot make it
        // reach the disk. The only observable effect of a read is the charge against the source budget.
        var pdf = PdfFixtures.Document(pageCount: 3);
        var source = new ByteArrayMediaSource(pdf);

        await new PdfMetadataPlugin().ExtractAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length), source, null, CancellationToken.None);

        Assert.Equal(pdf.Length, source.ChargedBytes);
        Assert.True(source.ReadCount < 64, $"A {pdf.Length} byte document took {source.ReadCount} host reads.");
    }

    internal static Task<MediaExtractionResult> ExtractAsync(byte[] pdf) =>
        new PdfMetadataPlugin().ExtractAsync(
            new MediaItem(".pdf", "application/pdf", pdf.Length),
            new ByteArrayMediaSource(pdf),
            progress: null,
            CancellationToken.None);

    internal static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    internal static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    internal static double? Number(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Number;

    internal static bool? Boolean(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Boolean;
}
