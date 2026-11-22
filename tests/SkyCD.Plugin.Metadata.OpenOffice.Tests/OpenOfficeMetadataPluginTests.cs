using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.OpenOffice.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.OpenOffice.Models;

namespace SkyCD.Plugin.Metadata.OpenOffice.Tests;

public class OpenOfficeMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsOpenOfficePackagesAndAVersionedSchema()
    {
        var descriptor = new OpenOfficeMetadataPlugin().Descriptor;

        Assert.Contains(".odt", descriptor.Extensions);
        Assert.Contains(".ods", descriptor.Extensions);
        Assert.Contains(".odp", descriptor.Extensions);
        Assert.Contains(".sxw", descriptor.Extensions);
        Assert.DoesNotContain(".otf", descriptor.Extensions);
        Assert.DoesNotContain(".otc", descriptor.Extensions);
        Assert.Contains("application/vnd.oasis.opendocument.text", descriptor.ContentTypes);
        Assert.Contains("application/vnd.sun.xml.writer", descriptor.ContentTypes);
        Assert.Equal(OpenOfficeSchema.MetadataSchemaId, descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task FullMetadata_IsPublishedFieldByField()
    {
        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument());

        Assert.Equal("odf", Text(result, OpenOfficeSchema.Container));
        Assert.Equal("text", Text(result, OpenOfficeSchema.DocumentType));
        Assert.Equal("1.2", Text(result, OpenOfficeSchema.FormatVersion));
        Assert.False(Boolean(result, OpenOfficeSchema.IsEncrypted));
        Assert.Equal("Annual Report", Text(result, OpenOfficeSchema.Title));
        Assert.Equal("Budget", Text(result, OpenOfficeSchema.Subject));
        Assert.Equal("Line one\nLine two", Text(result, OpenOfficeSchema.Description));
        Assert.Equal("A. Writer", Text(result, OpenOfficeSchema.Author));
        Assert.Equal("Initial Writer", Text(result, OpenOfficeSchema.InitialCreator));
        Assert.Equal("finance, annual", Text(result, OpenOfficeSchema.Keywords));
        Assert.Equal("LibreOffice/7.6.4.1", Text(result, OpenOfficeSchema.Generator));
        Assert.Equal("en-GB", Text(result, OpenOfficeSchema.Language));
        Assert.Equal("Internal use only", Text(result, OpenOfficeSchema.Rights));
        Assert.Equal("SkyCD Publishing", Text(result, OpenOfficeSchema.Publisher));
        Assert.Equal("Editor", Text(result, OpenOfficeSchema.Contributor));
        Assert.Equal("DOC-2024-17", Text(result, OpenOfficeSchema.Identifier));
        Assert.Equal("Intranet", Text(result, OpenOfficeSchema.Source));
        Assert.Equal("Series A", Text(result, OpenOfficeSchema.Relation));
        Assert.Equal("2024", Text(result, OpenOfficeSchema.Coverage));
        Assert.Equal("Report", Text(result, OpenOfficeSchema.Type));
        Assert.Equal("application/vnd.oasis.opendocument.text", Text(result, OpenOfficeSchema.Format));

        // The template's nested date must not be merged into its name.
        Assert.Equal("Report template", Text(result, OpenOfficeSchema.Template));
        Assert.Equal("Print Office", Text(result, OpenOfficeSchema.PrintedBy));

        Assert.Equal(new DateTimeOffset(2024, 3, 1, 12, 30, 15, TimeSpan.Zero),
            Timestamp(result, OpenOfficeSchema.CreatedAt));
        Assert.Equal(new DateTimeOffset(2024, 6, 4, 8, 9, 10, TimeSpan.FromHours(2)),
            Timestamp(result, OpenOfficeSchema.ModifiedAt));

        // The stored print date carries no offset; it is published as a nominal zero-offset time.
        Assert.Equal(new DateTimeOffset(2024, 7, 5, 10, 11, 12, TimeSpan.Zero),
            Timestamp(result, OpenOfficeSchema.PrintedAt));

        Assert.Equal(7L, Integer(result, OpenOfficeSchema.EditingCycles));
        Assert.Equal(150L, Integer(result, OpenOfficeSchema.EditingTimeMinutes));
        Assert.Equal(10L, Integer(result, OpenOfficeSchema.PageCount));
        Assert.Equal(2L, Integer(result, OpenOfficeSchema.TableCount));
        Assert.Equal(3L, Integer(result, OpenOfficeSchema.ImageCount));
        Assert.Equal(4L, Integer(result, OpenOfficeSchema.ObjectCount));
        Assert.Equal(55L, Integer(result, OpenOfficeSchema.ParagraphCount));
        Assert.Equal(1200L, Integer(result, OpenOfficeSchema.WordCount));
        Assert.Equal(7000L, Integer(result, OpenOfficeSchema.CharacterCount));
        Assert.Equal(0L, Integer(result, OpenOfficeSchema.CellCount));
        Assert.Equal(0L, Integer(result, OpenOfficeSchema.SheetCount));

        AssertSchemaConformance(result);
    }

    [Fact]
    public async Task LegacyOpenOfficePackage_UsesItsNamespacesAndGeneration()
    {
        var result = await ExtractAsync(OpenOfficeFixtures.LegacyDocument(), ".sxw",
            OpenOfficeFixtures.LegacyWriterContentType);

        Assert.Equal("openoffice", Text(result, OpenOfficeSchema.Container));
        Assert.Equal("text", Text(result, OpenOfficeSchema.DocumentType));
        Assert.Equal("1.0", Text(result, OpenOfficeSchema.FormatVersion));
        Assert.False(Boolean(result, OpenOfficeSchema.IsEncrypted));
        Assert.Equal("Legacy Report", Text(result, OpenOfficeSchema.Title));
        Assert.Equal("A. Writer", Text(result, OpenOfficeSchema.Author));
        Assert.Equal(150L, Integer(result, OpenOfficeSchema.EditingTimeMinutes));
        Assert.Equal(new DateTimeOffset(2024, 3, 1, 12, 30, 15, TimeSpan.Zero),
            Timestamp(result, OpenOfficeSchema.CreatedAt));
        AssertSchemaConformance(result);
    }

    [Fact]
    public async Task SpreadsheetContentType_DeterminesTheDocumentType()
    {
        var result = await ExtractAsync(
            OpenOfficeFixtures.OdfDocument(contentType: OpenOfficeFixtures.OdfSpreadsheetContentType),
            ".ods", OpenOfficeFixtures.OdfSpreadsheetContentType);

        Assert.Equal("spreadsheet", Text(result, OpenOfficeSchema.DocumentType));
        Assert.Equal("odf", Text(result, OpenOfficeSchema.Container));
    }

    [Fact]
    public async Task PackageWithoutMetadata_PublishesContainerFactsOnly()
    {
        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument(includeMetadata: false));

        Assert.Equal("odf", Text(result, OpenOfficeSchema.Container));
        Assert.Equal("text", Text(result, OpenOfficeSchema.DocumentType));
        Assert.False(Boolean(result, OpenOfficeSchema.IsEncrypted));
        Assert.Equal(3, result.Values.Count);
        Assert.Null(Text(result, OpenOfficeSchema.Title));
        Assert.Null(Text(result, OpenOfficeSchema.FormatVersion));
        AssertSchemaConformance(result);
    }

    [Fact]
    public async Task MissingMimeType_FallsBackToTheExtension()
    {
        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument(includeMimeType: false));

        Assert.Equal("odf", Text(result, OpenOfficeSchema.Container));
        Assert.Equal("text", Text(result, OpenOfficeSchema.DocumentType));
        Assert.Equal("Annual Report", Text(result, OpenOfficeSchema.Title));
    }

    [Fact]
    public async Task EmptyMetadataElement_IsTreatedAsMissingMetadata()
    {
        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument(OpenOfficeFixtures.OdfMetadata(string.Empty)));

        Assert.Equal("odf", Text(result, OpenOfficeSchema.Container));
        Assert.Equal("1.2", Text(result, OpenOfficeSchema.FormatVersion));
        Assert.Equal(4, result.Values.Count);
        Assert.Null(Text(result, OpenOfficeSchema.Title));
        AssertSchemaConformance(result);
    }

    [Fact]
    public async Task BlankAndInvalidValues_AreOmittedRatherThanGuessed()
    {
        const string body = """
            <dc:title>   </dc:title>
            <dc:subject>First</dc:subject>
            <dc:subject>Second</dc:subject>
            <dc:date>not-a-date</dc:date>
            <meta:creation-date>2024-13-45T99:99:99Z</meta:creation-date>
            <meta:editing-cycles>-2</meta:editing-cycles>
            <meta:editing-duration>nonsense</meta:editing-duration>
            <meta:document-statistic meta:page-count="-1" meta:word-count="abc" meta:character-count="42"/>
            """;

        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument(OpenOfficeFixtures.OdfMetadata(body)));

        Assert.Null(Text(result, OpenOfficeSchema.Title));
        Assert.Equal("First", Text(result, OpenOfficeSchema.Subject));
        Assert.Null(Timestamp(result, OpenOfficeSchema.ModifiedAt));
        Assert.Null(Timestamp(result, OpenOfficeSchema.CreatedAt));
        Assert.Null(Integer(result, OpenOfficeSchema.EditingCycles));
        Assert.Null(Integer(result, OpenOfficeSchema.EditingTimeMinutes));
        Assert.Null(Integer(result, OpenOfficeSchema.PageCount));
        Assert.Null(Integer(result, OpenOfficeSchema.WordCount));
        Assert.Equal(42L, Integer(result, OpenOfficeSchema.CharacterCount));
        AssertSchemaConformance(result);
    }

    [Fact]
    public async Task RepeatedField_FirstNonBlankValueWins()
    {
        const string body = """
            <dc:title>   </dc:title>
            <dc:title>Second</dc:title>
            <dc:title>Third</dc:title>
            """;

        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument(OpenOfficeFixtures.OdfMetadata(body)));

        Assert.Equal("Second", Text(result, OpenOfficeSchema.Title));
    }

    [Fact]
    public async Task DurationWithoutMinutes_IsPublishedAsZero()
    {
        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument(
            OpenOfficeFixtures.OdfMetadata("<meta:editing-duration>PT45S</meta:editing-duration>")));

        Assert.Equal(0L, Integer(result, OpenOfficeSchema.EditingTimeMinutes));
    }

    [Fact]
    public async Task PredefinedEntities_AreDecoded()
    {
        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument(
            OpenOfficeFixtures.OdfMetadata("<dc:title>Research &amp; Development</dc:title>")));

        Assert.Equal("Research & Development", Text(result, OpenOfficeSchema.Title));
    }

    [Fact]
    public async Task PlainManifest_KeepsThePackageUnencrypted()
    {
        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument(manifestXml: OpenOfficeFixtures.Manifest(false)));

        Assert.False(Boolean(result, OpenOfficeSchema.IsEncrypted));
        Assert.Equal("Annual Report", Text(result, OpenOfficeSchema.Title));
    }

    [Fact]
    public async Task EncryptedManifestWithReadableMetadata_MarksThePackage()
    {
        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument(manifestXml: OpenOfficeFixtures.Manifest(true)));

        Assert.True(Boolean(result, OpenOfficeSchema.IsEncrypted));
        Assert.Equal("Annual Report", Text(result, OpenOfficeSchema.Title));
    }

    [Fact]
    public async Task DamagedManifest_DoesNotSuppressReadableMetadata()
    {
        var result = await ExtractAsync(OpenOfficeFixtures.OdfDocument(manifestXml: "<manifest:manifest"));

        Assert.False(Boolean(result, OpenOfficeSchema.IsEncrypted));
        Assert.Equal("Annual Report", Text(result, OpenOfficeSchema.Title));
    }

    [Fact]
    public async Task EncryptedPackageWithoutReadableMetadata_ReportsAuthenticationRequired()
    {
        var document = OpenOfficeFixtures.OdfDocument(
            includeMetadata: false,
            manifestXml: OpenOfficeFixtures.Manifest(true));

        await Assert.ThrowsAsync<OpenOfficeAuthenticationRequiredException>(() => ExtractAsync(document));
    }

    [Fact]
    public async Task EncryptedPackageWithDamagedMetadata_ReportsAuthenticationRequired()
    {
        var document = OpenOfficeFixtures.OdfDocument(
            OpenOfficeFixtures.TruncatedMetadata(),
            manifestXml: OpenOfficeFixtures.Manifest(true));

        await Assert.ThrowsAsync<OpenOfficeAuthenticationRequiredException>(() => ExtractAsync(document));
    }

    [Fact]
    public async Task MetadataWithoutTheExpectedRoot_IsRejectedAsUnreadable()
    {
        var document = OpenOfficeFixtures.OdfDocument(
            OpenOfficeFixtures.MetadataWithoutRoot("<dc:title>Odd</dc:title>"));

        await Assert.ThrowsAsync<OpenOfficeDecodeException>(() => ExtractAsync(document));
    }

    [Fact]
    public async Task MalformedMetadata_IsRejectedAsUnreadable()
    {
        var document = OpenOfficeFixtures.OdfDocument(OpenOfficeFixtures.TruncatedMetadata());

        await Assert.ThrowsAsync<OpenOfficeDecodeException>(() => ExtractAsync(document));
    }

    [Fact]
    public async Task ExternalEntity_IsRejectedWithoutBeingResolved()
    {
        var document = OpenOfficeFixtures.OdfDocument(OpenOfficeFixtures.ExternalEntityMetadata());

        await Assert.ThrowsAsync<OpenOfficeDecodeException>(() => ExtractAsync(document));
    }

    [Fact]
    public async Task EmptySource_IsRejectedAsUnreadable()
    {
        await Assert.ThrowsAsync<OpenOfficeDecodeException>(() => ExtractAsync([]));
    }

    [Fact]
    public async Task NonZipSource_IsRejectedAsUnreadable()
    {
        await Assert.ThrowsAsync<OpenOfficeDecodeException>(() => ExtractAsync(OpenOfficeFixtures.NotAZip()));
    }

    [Fact]
    public async Task TruncatedPackage_IsRejectedAsUnreadable()
    {
        var document = OpenOfficeFixtures.Truncated(OpenOfficeFixtures.OdfDocument());

        await Assert.ThrowsAsync<OpenOfficeDecodeException>(() => ExtractAsync(document));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyRead()
    {
        var document = OpenOfficeFixtures.OdfDocument();
        var source = new ByteArrayMediaSource(document);

        var exception = await Assert.ThrowsAsync<OpenOfficeSourceTooLargeException>(() =>
            new OpenOfficeMetadataPlugin(new OpenOfficePluginOptions(maxSourceBytes: 100)).ExtractAsync(
                new MediaItem(".odt", null, document.Length), source, null, CancellationToken.None));

        Assert.Contains(document.Length.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task OversizedMetadata_IsSkippedAndContainerFactsStand()
    {
        var metadata = OpenOfficeFixtures.OdfMetadata(
            $"<dc:title>{new string('x', 2048)}</dc:title>");
        var document = OpenOfficeFixtures.OdfDocument(metadata);

        var result = await ExtractAsync(document, options: new OpenOfficePluginOptions(maxMetadataBytes: 256));

        Assert.Equal("odf", Text(result, OpenOfficeSchema.Container));
        Assert.Equal("text", Text(result, OpenOfficeSchema.DocumentType));
        Assert.False(Boolean(result, OpenOfficeSchema.IsEncrypted));
        Assert.Equal(3, result.Values.Count);
        Assert.Null(Text(result, OpenOfficeSchema.Title));
        AssertSchemaConformance(result);
    }

    [Fact]
    public async Task Cancellation_BeforeReading_ChargesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var document = OpenOfficeFixtures.OdfDocument();
        var source = new ByteArrayMediaSource(document);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new OpenOfficeMetadataPlugin().ExtractAsync(
                new MediaItem(".odt", null, document.Length), source, null, cancellation.Token));

        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task Source_IsReadOnceAndChargedExactlyOnce()
    {
        var document = OpenOfficeFixtures.OdfDocument();
        var source = new ByteArrayMediaSource(document);

        await new OpenOfficeMetadataPlugin().ExtractAsync(
            new MediaItem(".odt", null, document.Length), source, null, CancellationToken.None);

        Assert.Equal(document.Length, source.ChargedBytes);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task Progress_ReportsCompletion()
    {
        var progress = new CollectingProgress();
        var document = OpenOfficeFixtures.OdfDocument();
        var source = new ByteArrayMediaSource(document);

        await new OpenOfficeMetadataPlugin().ExtractAsync(
            new MediaItem(".odt", null, document.Length), source, progress, CancellationToken.None);

        Assert.NotEmpty(progress.Reports);
        Assert.Equal(1, progress.Reports[^1]);
    }

    private static async Task<MediaExtractionResult> ExtractAsync(
        byte[] document,
        string extension = ".odt",
        string? contentType = null,
        OpenOfficePluginOptions? options = null)
    {
        var source = new ByteArrayMediaSource(document);
        var plugin = options is null ? new OpenOfficeMetadataPlugin() : new OpenOfficeMetadataPlugin(options);
        return await plugin.ExtractAsync(
            new MediaItem(extension, contentType, document.Length), source, null, CancellationToken.None);
    }

    private static string? Text(MediaExtractionResult result, string key) =>
        result.Values.SingleOrDefault(value => value.Key == key)?.Text;

    private static bool? Boolean(MediaExtractionResult result, string key) =>
        result.Values.SingleOrDefault(value => value.Key == key)?.Boolean;

    private static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.SingleOrDefault(value => value.Key == key)?.Integer;

    private static DateTimeOffset? Timestamp(MediaExtractionResult result, string key) =>
        result.Values.SingleOrDefault(value => value.Key == key)?.Timestamp;

    private static void AssertSchemaConformance(MediaExtractionResult result)
    {
        var fields = new OpenOfficeMetadataPlugin().Descriptor.Schema.Fields
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
