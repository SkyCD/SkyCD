using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Office.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Tests;

public class OfficeMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsOfficeSourcesAndAVersionedSchema()
    {
        var descriptor = new OfficeMetadataPlugin().Descriptor;

        Assert.Contains(".docx", descriptor.Extensions);
        Assert.Contains(".xlsx", descriptor.Extensions);
        Assert.Contains(".pptx", descriptor.Extensions);
        Assert.Contains(".doc", descriptor.Extensions);
        Assert.Contains(".xls", descriptor.Extensions);
        Assert.Contains(".ppt", descriptor.Extensions);
        Assert.Contains("application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            descriptor.ContentTypes);
        Assert.Contains("application/msword", descriptor.ContentTypes);
        Assert.Equal("skycd.office.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task WordPackage_PublishesEveryCoreAndExtendedField()
    {
        var result = await ExtractAsync(OfficeFixtures.Docx(OfficeFixtures.FullCore(), OfficeFixtures.FullApp()));

        Assert.Equal("ooxml", Text(result, OfficeSchema.Container));
        Assert.Equal("word", Text(result, OfficeSchema.DocumentType));
        Assert.Equal("Quarterly Report", Text(result, OfficeSchema.Title));
        Assert.Equal("Figures", Text(result, OfficeSchema.Subject));
        Assert.Equal("A. Writer", Text(result, OfficeSchema.Author));
        Assert.Equal("finance; report", Text(result, OfficeSchema.Keywords));
        Assert.Equal("Reviewed once", Text(result, OfficeSchema.Comments));
        Assert.Equal("Reports", Text(result, OfficeSchema.Category));
        Assert.Equal("Final", Text(result, OfficeSchema.ContentStatus));
        Assert.Equal("Normal.dotm", Text(result, OfficeSchema.Template));
        Assert.Equal("Editor", Text(result, OfficeSchema.LastSavedBy));
        Assert.Equal("3", Text(result, OfficeSchema.RevisionNumber));
        Assert.Equal("en-GB", Text(result, OfficeSchema.Language));
        Assert.Equal("Microsoft Office Word", Text(result, OfficeSchema.ApplicationName));
        Assert.Equal("16.0000", Text(result, OfficeSchema.ApplicationVersion));
        Assert.Equal("SkyCD Ltd", Text(result, OfficeSchema.Company));
        Assert.Equal("M. Manager", Text(result, OfficeSchema.Manager));

        Assert.Equal(OfficeFixtures.Created, Timestamp(result, OfficeSchema.CreatedAt));
        Assert.Equal(OfficeFixtures.Modified, Timestamp(result, OfficeSchema.ModifiedAt));
        Assert.Equal(OfficeFixtures.LastPrinted, Timestamp(result, OfficeSchema.LastPrintedAt));

        Assert.Equal(5L, Integer(result, OfficeSchema.PageCount));
        Assert.Equal(1234L, Integer(result, OfficeSchema.WordCount));
        Assert.Equal(6789L, Integer(result, OfficeSchema.CharacterCount));
        Assert.Null(Integer(result, OfficeSchema.SlideCount));
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.IsEncrypted);
    }

    [Fact]
    public async Task WorkbookPackage_IsIdentifiedAsExcelWithItsStatistics()
    {
        var app = OfficeFixtures.App(
            ("Application", "Microsoft Excel"),
            ("Company", "SkyCD Ltd"),
            ("Words", "17"),
            ("Characters", "42"));

        var result = await ExtractAsync(OfficeFixtures.Xlsx(OfficeFixtures.Core(("dc:title", "Budget")), app));

        Assert.Equal("ooxml", Text(result, OfficeSchema.Container));
        Assert.Equal("excel", Text(result, OfficeSchema.DocumentType));
        Assert.Equal("Budget", Text(result, OfficeSchema.Title));
        Assert.Equal("Microsoft Excel", Text(result, OfficeSchema.ApplicationName));
        Assert.Equal(17L, Integer(result, OfficeSchema.WordCount));
        Assert.Equal(42L, Integer(result, OfficeSchema.CharacterCount));
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.PageCount);
    }

    [Fact]
    public async Task BinaryWorkbookPackage_IsIdentifiedAsExcel()
    {
        var result = await ExtractAsync(OfficeFixtures.Xlsb(null, OfficeFixtures.App(("Application", "Microsoft Excel"))));

        Assert.Equal("excel", Text(result, OfficeSchema.DocumentType));
    }

    [Fact]
    public async Task PresentationPackage_ReportsTheSlideCount()
    {
        var result = await ExtractAsync(OfficeFixtures.Pptx(null, OfficeFixtures.PresentationApp(12)));

        Assert.Equal("powerpoint", Text(result, OfficeSchema.DocumentType));
        Assert.Equal(12L, Integer(result, OfficeSchema.SlideCount));
        Assert.Null(Integer(result, OfficeSchema.PageCount));
    }

    [Fact]
    public async Task MissingMetadataParts_PublishOnlyTheContainerAndDocumentType()
    {
        var result = await ExtractAsync(OfficeFixtures.Docx());

        Assert.Equal(2, result.Values.Count);
        Assert.Equal("ooxml", Text(result, OfficeSchema.Container));
        Assert.Equal("word", Text(result, OfficeSchema.DocumentType));
    }

    [Fact]
    public async Task MetadataPartNames_AreMatchedCaseInsensitively()
    {
        var package = OfficeFixtures.Package("word/document.xml", null, null,
            ("docProps/Core.XML", OfficeFixtures.Core(("dc:title", "Upper case part"))));

        var result = await ExtractAsync(package);

        Assert.Equal("Upper case part", Text(result, OfficeSchema.Title));
    }

    [Fact]
    public async Task EncryptedWrapper_IsReportedWithoutDecrypting()
    {
        var result = await ExtractAsync(OfficeFixtures.EncryptedPackage());

        Assert.Equal("ooxml", Text(result, OfficeSchema.Container));
        Assert.True(result.Values.Single(value => value.Key == OfficeSchema.IsEncrypted).Boolean);
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.Title);
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.DocumentType);
    }

    [Fact]
    public async Task LegacyWordDocument_PublishesBothSummaryStreams()
    {
        var result = await ExtractAsync(OfficeFixtures.LegacyWord(OfficeFixtures.FullSummary()));

        Assert.Equal("ole", Text(result, OfficeSchema.Container));
        Assert.Equal("word", Text(result, OfficeSchema.DocumentType));
        Assert.Equal("Quarterly Report", Text(result, OfficeSchema.Title));
        Assert.Equal("Figures", Text(result, OfficeSchema.Subject));
        Assert.Equal("A. Writer", Text(result, OfficeSchema.Author));
        Assert.Equal("finance; report", Text(result, OfficeSchema.Keywords));
        Assert.Equal("Reviewed once", Text(result, OfficeSchema.Comments));
        Assert.Equal("Normal.dot", Text(result, OfficeSchema.Template));
        Assert.Equal("Editor", Text(result, OfficeSchema.LastSavedBy));
        Assert.Equal("3", Text(result, OfficeSchema.RevisionNumber));
        Assert.Equal("Microsoft Office Word", Text(result, OfficeSchema.ApplicationName));
        Assert.Equal("Reports", Text(result, OfficeSchema.Category));
        Assert.Equal("M. Manager", Text(result, OfficeSchema.Manager));
        Assert.Equal("SkyCD Ltd", Text(result, OfficeSchema.Company));
        Assert.Equal("Final", Text(result, OfficeSchema.ContentStatus));
        Assert.Equal("en-GB", Text(result, OfficeSchema.Language));
        Assert.Equal(OfficeFixtures.Created, Timestamp(result, OfficeSchema.CreatedAt));
        Assert.Equal(OfficeFixtures.Modified, Timestamp(result, OfficeSchema.ModifiedAt));
        Assert.Equal(OfficeFixtures.LastPrinted, Timestamp(result, OfficeSchema.LastPrintedAt));
        Assert.Equal(5L, Integer(result, OfficeSchema.PageCount));
        Assert.Equal(1234L, Integer(result, OfficeSchema.WordCount));
        Assert.Equal(6789L, Integer(result, OfficeSchema.CharacterCount));
    }

    [Theory]
    [InlineData("workbook", "excel")]
    [InlineData("book", "excel")]
    [InlineData("presentation", "powerpoint")]
    public async Task LegacyDocumentStreams_IdentifyTheOfficeApplication(string kind, string expectedType)
    {
        var document = kind switch
        {
            "workbook" => OfficeFixtures.LegacyWorkbook(),
            "book" => OfficeFixtures.LegacyBook(),
            _ => OfficeFixtures.LegacyPresentation()
        };

        var result = await ExtractAsync(document);

        Assert.Equal("ole", Text(result, OfficeSchema.Container));
        Assert.Equal(expectedType, Text(result, OfficeSchema.DocumentType));
    }

    [Fact]
    public async Task LegacyDocumentWithoutSummaryStreams_PublishesContainerAndTypeOnly()
    {
        var result = await ExtractAsync(OfficeFixtures.LegacyWorkbook());

        Assert.Equal(2, result.Values.Count);
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.Title);
    }

    [Fact]
    public async Task BlankValues_AreOmitted()
    {
        var core = OfficeFixtures.Core(("dc:title", "   "), ("dc:creator", string.Empty));

        var result = await ExtractAsync(OfficeFixtures.Docx(core, null));

        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.Title);
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.Author);
    }

    [Fact]
    public async Task ZeroAndNegativeStatistics_AreOmitted()
    {
        var app = OfficeFixtures.App(("Pages", "0"), ("Words", "-4"), ("Characters", "not a number"));

        var result = await ExtractAsync(OfficeFixtures.Docx(null, app));

        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.PageCount);
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.WordCount);
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.CharacterCount);
    }

    [Fact]
    public async Task WhitespaceInsideAValue_IsCollapsed()
    {
        var core = OfficeFixtures.Core(("dc:title", "Line one\r\nline two\t"));

        var result = await ExtractAsync(OfficeFixtures.Docx(core, null));

        Assert.Equal("Line one line two", Text(result, OfficeSchema.Title));
    }

    [Fact]
    public async Task DamagedCorePart_CostsOnlyTheCoreFields()
    {
        var package = OfficeFixtures.Docx("<cp:coreProperties><dc:title>x</closing>", OfficeFixtures.FullApp());

        var result = await ExtractAsync(package);

        Assert.Null(Text(result, OfficeSchema.Title));
        Assert.Equal("Microsoft Office Word", Text(result, OfficeSchema.ApplicationName));
        Assert.Equal(5L, Integer(result, OfficeSchema.PageCount));
    }

    [Fact]
    public async Task DocumentTypeDeclarations_AreNotExpandedOrResolved()
    {
        const string core =
            "<!DOCTYPE cp:coreProperties [<!ENTITY boom \"expanded\">]>" +
            "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\">" +
            "<dc:title xmlns:dc=\"http://purl.org/dc/elements/1.1/\">&boom;</dc:title></cp:coreProperties>";

        var result = await ExtractAsync(OfficeFixtures.Docx(core, OfficeFixtures.FullApp()));

        Assert.Null(Text(result, OfficeSchema.Title));
        Assert.Equal("Microsoft Office Word", Text(result, OfficeSchema.ApplicationName));
    }

    [Fact]
    public async Task NestedVectorFieldsInTheApplicationPart_AreIgnored()
    {
        const string app =
            "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"" +
            " xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
            "<Application>Microsoft Office Word</Application>" +
            "<HeadingPairs><vt:vector size=\"2\" baseType=\"variant\">" +
            "<vt:variant><vt:lpstr>Title</vt:lpstr></vt:variant>" +
            "<vt:variant><vt:i4>1</vt:i4></vt:variant></vt:vector></HeadingPairs>" +
            "<TitlesOfParts><vt:vector size=\"1\" baseType=\"lpstr\"><vt:lpstr>Section</vt:lpstr></vt:vector>" +
            "</TitlesOfParts></Properties>";

        var result = await ExtractAsync(OfficeFixtures.Docx(null, app));

        Assert.Equal("Microsoft Office Word", Text(result, OfficeSchema.ApplicationName));
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.PageCount);
    }

    [Theory]
    [InlineData("notoffice")]
    [InlineData("empty")]
    [InlineData("damagedpackage")]
    [InlineData("nodocumentpart")]
    [InlineData("damagedcompound")]
    [InlineData("noofficestream")]
    public async Task UnreadableSources_FailWithADecodeError(string kind)
    {
        var document = kind switch
        {
            "notoffice" => "This is not an Office document."u8.ToArray(),
            "empty" => [],
            "damagedpackage" => OfficeFixtures.Truncate(OfficeFixtures.Docx(), 100),
            "nodocumentpart" => OfficeFixtures.PackageWithoutDocumentPart(OfficeFixtures.FullCore()),
            "damagedcompound" => OfficeFixtures.DamageDirectory(OfficeFixtures.LegacyWorkbook()),
            _ => OfficeFixtures.CompoundWithoutOfficeStream()
        };

        await Assert.ThrowsAsync<OfficeDecodeException>(() => ExtractAsync(document));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var document = OfficeFixtures.Docx();
        var source = new ByteArrayMediaSource(document);
        var plugin = new OfficeMetadataPlugin(new OfficePluginOptions(maxSourceBytes: document.Length - 1));

        await Assert.ThrowsAsync<OfficeSourceTooLargeException>(() => plugin.ExtractAsync(
            Item(document), source, null, CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var document = OfficeFixtures.Docx();
        var source = new ByteArrayMediaSource(document);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OfficeMetadataPlugin().ExtractAsync(
            Item(document), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = OfficeSchema.Metadata;
        var result = await ExtractAsync(OfficeFixtures.Docx(OfficeFixtures.FullCore(), OfficeFixtures.FullApp()));

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
        var document = OfficeFixtures.Docx(OfficeFixtures.FullCore(), OfficeFixtures.FullApp());
        var before = (byte[])document.Clone();

        await ExtractAsync(document);

        Assert.Equal(before, document);
    }

    [Fact]
    public async Task ReadingChargesTheSourceOnceAndNeverTouchesTheFilesystem()
    {
        var document = OfficeFixtures.LegacyWord(OfficeFixtures.FullSummary());
        var source = new ByteArrayMediaSource(document);

        await new OfficeMetadataPlugin().ExtractAsync(
            Item(document), source, null, CancellationToken.None);

        Assert.Equal(document.Length, source.ChargedBytes);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task Progress_IsReportedAndEndsAtCompletion()
    {
        var reports = new CollectingProgress();
        var document = OfficeFixtures.Docx(OfficeFixtures.FullCore(), OfficeFixtures.FullApp());

        await new OfficeMetadataPlugin().ExtractAsync(
            Item(document), new ByteArrayMediaSource(document), reports, CancellationToken.None);

        Assert.NotEmpty(reports.Values);
        Assert.Equal(1, reports.Values[^1]);
        Assert.All(reports.Values, value => Assert.InRange(value, 0, 1));
    }

    internal static Task<MediaExtractionResult> ExtractAsync(byte[] document) =>
        new OfficeMetadataPlugin().ExtractAsync(
            Item(document),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

    internal static MediaItem Item(byte[] document) =>
        new(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", document.Length);

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
