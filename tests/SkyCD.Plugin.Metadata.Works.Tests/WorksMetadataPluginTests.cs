using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Works.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.Works.Models;

namespace SkyCD.Plugin.Metadata.Works.Tests;

public class WorksMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsOnlyWorksWordProcessorSourcesAndAVersionedSchema()
    {
        var descriptor = new WorksMetadataPlugin().Descriptor;

        Assert.Equal([".wps"], descriptor.Extensions);
        Assert.Equal(["application/vnd.ms-works"], descriptor.ContentTypes);
        Assert.Equal("skycd.works.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task CompoundDocument_PublishesHeaderFactsAndEverySummaryField()
    {
        var document = WorksFixtures.Works4(WorksFixtures.FullSummary(), WorksFixtures.FullDocumentSummary());

        var result = await ExtractAsync(document);

        Assert.Equal("works", Text(result, WorksSchema.Container));
        Assert.Equal("compound", Text(result, WorksSchema.Storage));
        Assert.Equal("4", Text(result, WorksSchema.FormatVersion));
        Assert.Equal("Quarterly Report", Text(result, WorksSchema.Title));
        Assert.Equal("Figures", Text(result, WorksSchema.Subject));
        Assert.Equal("A. Writer", Text(result, WorksSchema.Author));
        Assert.Equal("finance; report", Text(result, WorksSchema.Keywords));
        Assert.Equal("Reviewed once", Text(result, WorksSchema.Comments));
        Assert.Equal("Normal.dot", Text(result, WorksSchema.Template));
        Assert.Equal("Editor", Text(result, WorksSchema.LastSavedBy));
        Assert.Equal("3", Text(result, WorksSchema.RevisionNumber));
        Assert.Equal("Microsoft Works", Text(result, WorksSchema.ApplicationName));
        Assert.Equal("Reports", Text(result, WorksSchema.Category));
        Assert.Equal("M. Manager", Text(result, WorksSchema.Manager));
        Assert.Equal("SkyCD Ltd", Text(result, WorksSchema.Company));
        Assert.Equal("Final", Text(result, WorksSchema.ContentStatus));
        Assert.Equal("en-GB", Text(result, WorksSchema.Language));

        Assert.Equal(WorksFixtures.Created, Timestamp(result, WorksSchema.CreatedAt));
        Assert.Equal(WorksFixtures.Modified, Timestamp(result, WorksSchema.ModifiedAt));
        Assert.Equal(WorksFixtures.LastPrinted, Timestamp(result, WorksSchema.LastPrintedAt));

        Assert.Equal(5L, Integer(result, WorksSchema.PageCount));
        Assert.Equal(1234L, Integer(result, WorksSchema.WordCount));
        Assert.Equal(6789L, Integer(result, WorksSchema.CharacterCount));
    }

    [Fact]
    public async Task Works3_IsIdentifiedFromItsMn0Header()
    {
        var result = await ExtractAsync(WorksFixtures.Works3(WorksFixtures.Text(2, "Old document")));

        Assert.Equal("3", Text(result, WorksSchema.FormatVersion));
        Assert.Equal("Old document", Text(result, WorksSchema.Title));
    }

    [Theory]
    [InlineData("2000")]
    [InlineData("7/8")]
    public async Task ContentsBasedDocuments_ReportTheChunkGeneration(string expectedVersion)
    {
        var document = expectedVersion == "2000"
            ? WorksFixtures.Works2000(WorksFixtures.Text(2, "Works 2000"))
            : WorksFixtures.Works7(WorksFixtures.Text(2, "Works 7"));

        var result = await ExtractAsync(document);

        Assert.Equal("compound", Text(result, WorksSchema.Storage));
        Assert.Equal(expectedVersion, Text(result, WorksSchema.FormatVersion));
        Assert.Equal(expectedVersion == "2000" ? "Works 2000" : "Works 7", Text(result, WorksSchema.Title));
    }

    [Fact]
    public async Task FlatDocument_IsReportedWithHeaderFactsOnly()
    {
        var result = await ExtractAsync(WorksFixtures.Flat(version: 2));

        Assert.Equal("works", Text(result, WorksSchema.Container));
        Assert.Equal("flat", Text(result, WorksSchema.Storage));
        Assert.Equal("2", Text(result, WorksSchema.FormatVersion));
        Assert.DoesNotContain(result.Values, value => value.Key == WorksSchema.Title);
    }

    [Fact]
    public async Task FlatWorks3Header_ReportsTheLaterGeneration()
    {
        var result = await ExtractAsync(WorksFixtures.Flat(version: 5));

        Assert.Equal("3", Text(result, WorksSchema.FormatVersion));
    }

    [Fact]
    public async Task CompoundDocumentWithoutSummaryStreams_PublishesHeaderFactsOnly()
    {
        var result = await ExtractAsync(WorksFixtures.Works3());

        Assert.Equal(3, result.Values.Count);
        Assert.All(result.Values, value => Assert.Equal(MediaMetadataType.Text, value.Type));
        Assert.Equal("compound", Text(result, WorksSchema.Storage));
    }

    [Fact]
    public async Task DocumentSummaryStream_ContributesDedicatedFields()
    {
        SummaryProperty[] summary = [WorksFixtures.Codepage(), WorksFixtures.Text(2, "Shared title")];
        SummaryProperty[] documentSummary = [WorksFixtures.Codepage(), WorksFixtures.Text(2, "Shared category")];

        var result = await ExtractAsync(WorksFixtures.Works4(summary, documentSummary));

        Assert.Equal("Shared title", Text(result, WorksSchema.Title));
        Assert.Equal("Shared category", Text(result, WorksSchema.Category));
    }

    [Fact]
    public async Task AnsiValues_AreDecodedThroughTheStreamCodepage()
    {
        SummaryProperty[] summary =
        [
            WorksFixtures.Codepage(1250),
            WorksFixtures.AnsiText(2, "Příloha", 1250),
            WorksFixtures.AnsiText(4, "Žluťoučký", 1250)
        ];

        var result = await ExtractAsync(WorksFixtures.Works3(summary, []));

        Assert.Equal("Příloha", Text(result, WorksSchema.Title));
        Assert.Equal("Žluťoučký", Text(result, WorksSchema.Author));
    }

    [Fact]
    public async Task IntegerVariants_ArePublishedAsTextOrIntegersByField()
    {
        SummaryProperty[] summary =
        [
            WorksFixtures.Codepage(),
            WorksFixtures.Int2(9, 12),
            WorksFixtures.Int2(14, 2),
            WorksFixtures.Int4(16, 4096)
        ];

        var result = await ExtractAsync(WorksFixtures.Works3(summary, []));

        Assert.Equal("12", Text(result, WorksSchema.RevisionNumber));
        Assert.Equal(2L, Integer(result, WorksSchema.PageCount));
        Assert.Equal(4096L, Integer(result, WorksSchema.CharacterCount));
    }

    [Fact]
    public async Task ZeroStatistics_AreOmittedRatherThanPublishedAsZero()
    {
        SummaryProperty[] summary =
        [
            WorksFixtures.Codepage(),
            WorksFixtures.Int4(14, 0),
            WorksFixtures.Int4(15, 0)
        ];

        var result = await ExtractAsync(WorksFixtures.Works3(summary, []));

        Assert.DoesNotContain(result.Values, value => value.Key == WorksSchema.PageCount);
        Assert.DoesNotContain(result.Values, value => value.Key == WorksSchema.WordCount);
    }

    [Fact]
    public async Task BlankValues_AreOmitted()
    {
        SummaryProperty[] summary =
        [
            WorksFixtures.Codepage(),
            WorksFixtures.Text(2, "   "),
            WorksFixtures.Text(4, string.Empty)
        ];

        var result = await ExtractAsync(WorksFixtures.Works3(summary, []));

        Assert.DoesNotContain(result.Values, value => value.Key == WorksSchema.Title);
        Assert.DoesNotContain(result.Values, value => value.Key == WorksSchema.Author);
    }

    [Fact]
    public async Task WhitespaceInsideAValue_IsCollapsed()
    {
        SummaryProperty[] summary = [WorksFixtures.Codepage(), WorksFixtures.Text(2, "Line one\r\nline two\t")];

        var result = await ExtractAsync(WorksFixtures.Works3(summary, []));

        Assert.Equal("Line one line two", Text(result, WorksSchema.Title));
    }

    [Fact]
    public async Task LargeHeaderStream_IsReadThroughRegularSectors()
    {
        var result = await ExtractAsync(WorksFixtures.Works4WithLargeHeader(WorksFixtures.Text(2, "Large")));

        Assert.Equal("4", Text(result, WorksSchema.FormatVersion));
        Assert.Equal("Large", Text(result, WorksSchema.Title));
    }

    [Fact]
    public async Task DamagedSummaryStream_CostsOnlyTheOptionalFields()
    {
        var result = await ExtractAsync(WorksFixtures.Works3WithDamagedSummary());

        Assert.Equal("works", Text(result, WorksSchema.Container));
        Assert.Equal("compound", Text(result, WorksSchema.Storage));
        Assert.Equal("3", Text(result, WorksSchema.FormatVersion));
        Assert.DoesNotContain(result.Values, value => value.Key == WorksSchema.Title);
    }

    [Theory]
    [InlineData("damagedheader")]
    [InlineData("notworks")]
    [InlineData("noworksstream")]
    [InlineData("truncated")]
    [InlineData("empty")]
    public async Task UnreadableSources_FailWithADecodeError(string kind)
    {
        var document = kind switch
        {
            "damagedheader" => WorksFixtures.Works3WithDamagedHeader(),
            "notworks" => WorksFixtures.NotWorks(),
            "noworksstream" => WorksFixtures.CompoundWithoutWorksStream(
                new CompoundStream("Other", new byte[128])),
            "truncated" => WorksFixtures.Truncate(WorksFixtures.Works4(
                WorksFixtures.FullSummary(), WorksFixtures.FullDocumentSummary()), 300),
            _ => []
        };

        await Assert.ThrowsAsync<WorksDecodeException>(() => ExtractAsync(document));
    }

    [Fact]
    public async Task EncryptedWrapper_ReportsAuthenticationRequired()
    {
        await Assert.ThrowsAsync<WorksAuthenticationRequiredException>(
            () => ExtractAsync(WorksFixtures.EncryptedWrapper()));
    }

    [Fact]
    public async Task DamagedStreamChain_FailsWithADecodeError()
    {
        // Pointing the first stream at a sector beyond the file leaves a chain the reader must reject.
        var document = WorksFixtures.Works3(WorksFixtures.Text(2, "Broken chain"));
        document[512 + 512 + 128 + 116] = 0xFF;

        await Assert.ThrowsAsync<WorksDecodeException>(() => ExtractAsync(document));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var document = WorksFixtures.Works3();
        var source = new ByteArrayMediaSource(document);
        var plugin = new WorksMetadataPlugin(new WorksPluginOptions(maxSourceBytes: document.Length - 1));

        await Assert.ThrowsAsync<WorksSourceTooLargeException>(() => plugin.ExtractAsync(
            Item(document), source, null, CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var document = WorksFixtures.Works3();
        var source = new ByteArrayMediaSource(document);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WorksMetadataPlugin().ExtractAsync(
            Item(document), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = WorksSchema.Metadata;
        var result = await ExtractAsync(WorksFixtures.Works4(
            WorksFixtures.FullSummary(), WorksFixtures.FullDocumentSummary()));

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
        var document = WorksFixtures.Works4(WorksFixtures.FullSummary(), WorksFixtures.FullDocumentSummary());
        var before = (byte[])document.Clone();

        await ExtractAsync(document);

        Assert.Equal(before, document);
    }

    [Fact]
    public async Task ReadingChargesTheSourceOnceAndNeverTouchesTheFilesystem()
    {
        var document = WorksFixtures.Works4(WorksFixtures.FullSummary(), WorksFixtures.FullDocumentSummary());
        var source = new ByteArrayMediaSource(document);

        await new WorksMetadataPlugin().ExtractAsync(
            Item(document), source, null, CancellationToken.None);

        Assert.Equal(document.Length, source.ChargedBytes);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task Progress_IsReportedAndEndsAtCompletion()
    {
        var reports = new CollectingProgress();
        var document = WorksFixtures.Works3(WorksFixtures.Text(2, "Progress"));

        await new WorksMetadataPlugin().ExtractAsync(
            Item(document), new ByteArrayMediaSource(document), reports, CancellationToken.None);

        Assert.NotEmpty(reports.Values);
        Assert.Equal(1, reports.Values[^1]);
        Assert.All(reports.Values, value => Assert.InRange(value, 0, 1));
    }

    internal static Task<MediaExtractionResult> ExtractAsync(byte[] document) =>
        new WorksMetadataPlugin().ExtractAsync(
            Item(document),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

    internal static MediaItem Item(byte[] document) => new(".wps", "application/vnd.ms-works", document.Length);

    internal static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    internal static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    internal static DateTimeOffset? Timestamp(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Timestamp;

    private sealed class CollectingProgress : IProgress<MediaProgress>
    {
        public System.Collections.Generic.List<double> Values { get; } = [];

        public void Report(MediaProgress value) => Values.Add(value.Fraction);
    }
}
