using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.WordPerfect.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.WordPerfect.Models;

namespace SkyCD.Plugin.Metadata.WordPerfect.Tests;

public class WordPerfectMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsWordPerfectDocumentsAndAVersionedSchema()
    {
        var descriptor = new WordPerfectMetadataPlugin().Descriptor;

        Assert.Equal([".wpd", ".wpt"], descriptor.Extensions);
        Assert.Equal(["application/vnd.wordperfect"], descriptor.ContentTypes);
        Assert.Equal(WordPerfectSchema.MetadataSchemaId, descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task ExtendedSummary_IsPublishedFieldByField()
    {
        var result = await ExtractAsync(WordPerfectFixtures.Document(
            WordPerfectFixtures.TextGroup(17, null, "Annual Report"),
            WordPerfectFixtures.TextGroup(46, null, "Budget"),
            WordPerfectFixtures.TextGroup(5, null, "A. Writer"),
            WordPerfectFixtures.TextGroup(26, null, "finance, annual"),
            WordPerfectFixtures.TextGroup(10, null, "Reports"),
            WordPerfectFixtures.TextGroup(1, null, "A summary of the year."),
            WordPerfectFixtures.TextGroup(13, null, "Reviewed by finance."),
            WordPerfectFixtures.TextGroup(30, null, "Head Office"),
            WordPerfectFixtures.TextGroup(16, null, "Accounting"),
            WordPerfectFixtures.TextGroup(21, null, "Commercial"),
            WordPerfectFixtures.TextGroup(31, null, "The Board"),
            WordPerfectFixtures.TextGroup(32, null, "SkyCD"),
            WordPerfectFixtures.TextGroup(33, null, "SkyCD Publishing"),
            WordPerfectFixtures.TextGroup(27, null, "en-GB"),
            WordPerfectFixtures.TextGroup(44, null, "Internal"),
            WordPerfectFixtures.TextGroup(45, null, "Final"),
            WordPerfectFixtures.TextGroup(43, null, "Confidential"),
            WordPerfectFixtures.TextGroup(34, null, "Informing"),
            WordPerfectFixtures.TextGroup(41, null, "7"),
            WordPerfectFixtures.TextGroup(51, null, "3.1"),
            WordPerfectFixtures.TextGroup(22, null, "DOC-2024-17"),
            WordPerfectFixtures.DateGroup(14, 2024, 3, 1),
            WordPerfectFixtures.DateGroup(15, 2024, 4, 2),
            WordPerfectFixtures.DateGroup(37, 2024, 5, 3),
            WordPerfectFixtures.DateGroup(39, 2024, 6, 4),
            WordPerfectFixtures.DateGroup(49, 2024, 7, 5)));

        Assert.Equal("wpd", Text(result, WordPerfectSchema.Container));
        Assert.Equal("flat", Text(result, WordPerfectSchema.Storage));
        Assert.Equal("6.1+", Text(result, WordPerfectSchema.FormatVersion));
        Assert.False(Boolean(result, WordPerfectSchema.IsEncrypted));
        Assert.Equal("Annual Report", Text(result, WordPerfectSchema.Title));
        Assert.Equal("Budget", Text(result, WordPerfectSchema.Subject));
        Assert.Equal("A. Writer", Text(result, WordPerfectSchema.Author));
        Assert.Equal("finance, annual", Text(result, WordPerfectSchema.Keywords));
        Assert.Equal("Reports", Text(result, WordPerfectSchema.Category));
        Assert.Equal("A summary of the year.", Text(result, WordPerfectSchema.Abstract));
        Assert.Equal("Reviewed by finance.", Text(result, WordPerfectSchema.Comments));
        Assert.Equal("Head Office", Text(result, WordPerfectSchema.Office));
        Assert.Equal("Accounting", Text(result, WordPerfectSchema.Department));
        Assert.Equal("Commercial", Text(result, WordPerfectSchema.Division));
        Assert.Equal("The Board", Text(result, WordPerfectSchema.Owner));
        Assert.Equal("SkyCD", Text(result, WordPerfectSchema.Project));
        Assert.Equal("SkyCD Publishing", Text(result, WordPerfectSchema.Publisher));
        Assert.Equal("en-GB", Text(result, WordPerfectSchema.Language));
        Assert.Equal("Internal", Text(result, WordPerfectSchema.Source));
        Assert.Equal("Final", Text(result, WordPerfectSchema.Status));
        Assert.Equal("Confidential", Text(result, WordPerfectSchema.Security));
        Assert.Equal("Informing", Text(result, WordPerfectSchema.Purpose));
        Assert.Equal("7", Text(result, WordPerfectSchema.RevisionNumber));
        Assert.Equal("3.1", Text(result, WordPerfectSchema.VersionNumber));
        Assert.Equal("DOC-2024-17", Text(result, WordPerfectSchema.DocumentNumber));

        Assert.Equal(new DateTimeOffset(2024, 3, 1, 12, 30, 15, TimeSpan.Zero),
            Timestamp(result, WordPerfectSchema.CreatedAt));
        Assert.Equal(new DateTimeOffset(2024, 4, 2, 12, 30, 15, TimeSpan.Zero),
            Timestamp(result, WordPerfectSchema.CompletedAt));
        Assert.Equal(new DateTimeOffset(2024, 5, 3, 12, 30, 15, TimeSpan.Zero),
            Timestamp(result, WordPerfectSchema.RecordedAt));
        Assert.Equal(new DateTimeOffset(2024, 6, 4, 12, 30, 15, TimeSpan.Zero),
            Timestamp(result, WordPerfectSchema.ModifiedAt));
        Assert.Equal(new DateTimeOffset(2024, 7, 5, 12, 30, 15, TimeSpan.Zero),
            Timestamp(result, WordPerfectSchema.VersionDate));

        AssertSchemaConformance(result);
    }

    [Fact]
    public async Task DocumentWithoutSummary_ReportsOnlyHeaderFacts()
    {
        var result = await ExtractAsync(WordPerfectFixtures.WithoutSummary());

        Assert.Equal("wpd", Text(result, WordPerfectSchema.Container));
        Assert.Equal("flat", Text(result, WordPerfectSchema.Storage));
        Assert.Equal("6.1+", Text(result, WordPerfectSchema.FormatVersion));
        Assert.False(Boolean(result, WordPerfectSchema.IsEncrypted));
        Assert.Equal(4, result.Values.Count);
        Assert.Null(Text(result, WordPerfectSchema.Title));
        AssertSchemaConformance(result);
    }

    [Fact]
    public async Task VersionSixPointZero_IsReportedFromTheHeader()
    {
        var result = await ExtractAsync(WordPerfectFixtures.WithSummary([], minorVersion: 0));

        Assert.Equal("6.0", Text(result, WordPerfectSchema.FormatVersion));
    }

    [Fact]
    public async Task UnsupportedWordPerfectFiveVariant_IsIndexedWithoutSummaryFields()
    {
        var result = await ExtractAsync(WordPerfectFixtures.WithSummary(
            WordPerfectFixtures.Summary(WordPerfectFixtures.TextGroup(17, null, "Ignored")),
            majorVersion: 0,
            minorVersion: 0));

        Assert.Equal("5", Text(result, WordPerfectSchema.FormatVersion));
        Assert.Null(Text(result, WordPerfectSchema.Title));
        AssertSchemaConformance(result);
    }

    [Fact]
    public async Task InvalidDateGroup_IsOmittedRatherThanGuessed()
    {
        var result = await ExtractAsync(WordPerfectFixtures.Document(
            WordPerfectFixtures.DateGroup(14, 2024, 3, 1),
            WordPerfectFixtures.DateGroup(15, 2024, 0, 0),
            WordPerfectFixtures.DateGroup(37, 1899, 12, 31)));

        Assert.Equal(new DateTimeOffset(2024, 3, 1, 12, 30, 15, TimeSpan.Zero),
            Timestamp(result, WordPerfectSchema.CreatedAt));
        Assert.Null(Timestamp(result, WordPerfectSchema.CompletedAt));
        Assert.Null(Timestamp(result, WordPerfectSchema.RecordedAt));
    }

    [Fact]
    public async Task FirstGroupForATag_Wins()
    {
        var result = await ExtractAsync(WordPerfectFixtures.Document(
            WordPerfectFixtures.TextGroup(17, null, "First"),
            WordPerfectFixtures.TextGroup(17, null, "Second")));

        Assert.Equal("First", Text(result, WordPerfectSchema.Title));
    }

    [Fact]
    public async Task ZeroLengthGroup_EndsTheSummary()
    {
        var result = await ExtractAsync(WordPerfectFixtures.Document(
            WordPerfectFixtures.TextGroup(17, null, "Before"),
            WordPerfectFixtures.EmptyGroup(),
            WordPerfectFixtures.TextGroup(46, null, "After")));

        Assert.Equal("Before", Text(result, WordPerfectSchema.Title));
        Assert.Null(Text(result, WordPerfectSchema.Subject));
    }

    [Fact]
    public async Task GroupThatOverrunsThePacket_EndsParsingButKeepsEarlierFields()
    {
        var overrunning = WordPerfectFixtures.WithDeclaredLength(
            WordPerfectFixtures.TextGroup(46, null, "Unreachable"), 60000);

        var result = await ExtractAsync(WordPerfectFixtures.Document(
            WordPerfectFixtures.TextGroup(17, null, "Kept"),
            overrunning));

        Assert.Equal("Kept", Text(result, WordPerfectSchema.Title));
        Assert.Null(Text(result, WordPerfectSchema.Subject));
    }

    [Fact]
    public async Task SummaryBeyondTheEndOfTheFile_IsSkipped()
    {
        var document = WordPerfectFixtures.Truncate(
            WordPerfectFixtures.Document(WordPerfectFixtures.TextGroup(17, null, "Lost")), 3);

        var result = await ExtractAsync(document);

        Assert.Equal("wpd", Text(result, WordPerfectSchema.Container));
        Assert.Null(Text(result, WordPerfectSchema.Title));
    }

    [Fact]
    public async Task MultinationalCharacters_AreDecoded()
    {
        var result = await ExtractAsync(WordPerfectFixtures.Document(
            WordPerfectFixtures.TextGroup(5, null, "M\u00FCller", characterSet: 1)));

        Assert.Equal("M\u00FCller", Text(result, WordPerfectSchema.Author));
    }

    [Fact]
    public async Task LineBreaksAndOuterWhitespace_AreFolded()
    {
        var result = await ExtractAsync(WordPerfectFixtures.Document(
            WordPerfectFixtures.TextGroup(13, null, "  First\r\nSecond\tThird  ")));

        Assert.Equal("First Second Third", Text(result, WordPerfectSchema.Comments));
    }

    [Fact]
    public async Task ByteOrderMarkAndHighBytes_DoNotBreakEarlierFields()
    {
        var result = await ExtractAsync(WordPerfectFixtures.Document(
            WordPerfectFixtures.TextGroup(17, null, "Title"),
            WordPerfectFixtures.TextGroup(46, null, "Caf\u00E9", characterSet: 1)));

        Assert.Equal("Title", Text(result, WordPerfectSchema.Title));
        Assert.Equal("Caf\u00E9", Text(result, WordPerfectSchema.Subject));
    }

    [Fact]
    public async Task EncryptedDocument_ReportsAuthenticationRequired()
    {
        var document = WordPerfectFixtures.WithSummary([], encryption: 1);

        await Assert.ThrowsAsync<WordPerfectAuthenticationRequiredException>(
            () => ExtractAsync(document));
    }

    [Fact]
    public async Task EmptySource_IsRejectedAsUnreadable()
    {
        await Assert.ThrowsAsync<WordPerfectDecodeException>(() => ExtractAsync([]));
    }

    [Fact]
    public async Task NonWordPerfectBytes_AreRejectedAsUnreadable()
    {
        await Assert.ThrowsAsync<WordPerfectDecodeException>(() => ExtractAsync("not a document"u8.ToArray()));
    }

    [Fact]
    public async Task TruncatedHeader_IsRejectedAsUnreadable()
    {
        await Assert.ThrowsAsync<WordPerfectDecodeException>(
            () => ExtractAsync([0xFF, (byte)'W', (byte)'P', (byte)'C', 0, 0, 0]));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyRead()
    {
        var document = WordPerfectFixtures.WithoutSummary();
        var source = new ByteArrayMediaSource(document);

        var exception = await Assert.ThrowsAsync<WordPerfectSourceTooLargeException>(() =>
            new WordPerfectMetadataPlugin(new WordPerfectPluginOptions(maxSourceBytes: 100)).ExtractAsync(
                new MediaItem(".wpd", null, document.Length), source, null, CancellationToken.None));

        Assert.Contains(document.Length.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task Cancellation_BeforeReading_ChargesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var document = WordPerfectFixtures.WithoutSummary();
        var source = new ByteArrayMediaSource(document);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WordPerfectMetadataPlugin().ExtractAsync(
                new MediaItem(".wpd", null, document.Length), source, null, cancellation.Token));

        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task Source_IsReadOnceAndChargedExactlyOnce()
    {
        var document = WordPerfectFixtures.Document(WordPerfectFixtures.TextGroup(17, null, "Title"));
        var source = new ByteArrayMediaSource(document);

        await new WordPerfectMetadataPlugin().ExtractAsync(
            new MediaItem(".wpd", null, document.Length), source, null, CancellationToken.None);

        Assert.Equal(document.Length, source.ChargedBytes);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task CompoundDocument_IsUnwrappedThroughPerfectOfficeMain()
    {
        var document = WordPerfectFixtures.Document(WordPerfectFixtures.TextGroup(17, null, "Wrapped"));
        var compound = WordPerfectFixtures.WrapInCompoundDocument(document);

        var result = await ExtractAsync(compound);

        Assert.Equal("compound", Text(result, WordPerfectSchema.Storage));
        Assert.Equal("Wrapped", Text(result, WordPerfectSchema.Title));
        AssertSchemaConformance(result);
    }

    [Fact]
    public async Task CompoundDocumentWithMiniStream_IsUnwrapped()
    {
        var document = WordPerfectFixtures.Document(WordPerfectFixtures.TextGroup(5, null, "Mini"));
        var compound = WordPerfectFixtures.WrapInCompoundDocumentUsingMiniStream(document);

        var result = await ExtractAsync(compound);

        Assert.Equal("compound", Text(result, WordPerfectSchema.Storage));
        Assert.Equal("Mini", Text(result, WordPerfectSchema.Author));
    }

    [Fact]
    public async Task CompoundDocumentWithoutTheWordPerfectStream_IsRejected()
    {
        var compound = WordPerfectFixtures.WrapInCompoundDocument(
            WordPerfectFixtures.WithoutSummary(), streamName: "SomeOtherStream");

        await Assert.ThrowsAsync<WordPerfectDecodeException>(() => ExtractAsync(compound));
    }

    [Fact]
    public async Task DamagedCompoundDocument_IsRejectedAsUnreadable()
    {
        var compound = WordPerfectFixtures.Truncate(
            WordPerfectFixtures.WrapInCompoundDocument(WordPerfectFixtures.WithoutSummary()), 512);

        await Assert.ThrowsAsync<WordPerfectDecodeException>(() => ExtractAsync(compound));
    }

    [Fact]
    public async Task Progress_ReportsCompletion()
    {
        var progress = new CollectingProgress();
        var document = WordPerfectFixtures.WithoutSummary();
        var source = new ByteArrayMediaSource(document);

        await new WordPerfectMetadataPlugin().ExtractAsync(
            new MediaItem(".wpd", null, document.Length), source, progress, CancellationToken.None);

        Assert.NotEmpty(progress.Reports);
        Assert.Equal(1, progress.Reports[^1]);
    }

    private static async Task<MediaExtractionResult> ExtractAsync(byte[] document)
    {
        var source = new ByteArrayMediaSource(document);
        return await new WordPerfectMetadataPlugin().ExtractAsync(
            new MediaItem(".wpd", "application/vnd.wordperfect", document.Length), source, null,
            CancellationToken.None);
    }

    private static string? Text(MediaExtractionResult result, string key) =>
        result.Values.SingleOrDefault(value => value.Key == key)?.Text;

    private static bool? Boolean(MediaExtractionResult result, string key) =>
        result.Values.SingleOrDefault(value => value.Key == key)?.Boolean;

    private static DateTimeOffset? Timestamp(MediaExtractionResult result, string key) =>
        result.Values.SingleOrDefault(value => value.Key == key)?.Timestamp;

    private static void AssertSchemaConformance(MediaExtractionResult result)
    {
        var fields = new WordPerfectMetadataPlugin().Descriptor.Schema.Fields
            .ToDictionary(field => field.Key, StringComparer.Ordinal);

        Assert.Equal(result.Values.Count,
            result.Values.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count());

        foreach (var value in result.Values)
        {
            Assert.True(fields.TryGetValue(value.Key, out var field), $"Unknown key {value.Key}.");
            Assert.Equal(field!.Type, value.Type);
            var payloads = new object?[] { value.Text, value.Integer, value.Number, value.Boolean, value.Timestamp };
            Assert.Equal(1, payloads.Count(payload => payload is not null));
        }
    }

    private sealed class CollectingProgress : IProgress<MediaProgress>
    {
        public List<double> Reports { get; } = [];

        public void Report(MediaProgress value) => Reports.Add(value.Fraction);
    }
}
