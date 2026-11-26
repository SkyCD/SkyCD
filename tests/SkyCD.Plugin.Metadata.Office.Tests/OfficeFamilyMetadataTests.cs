using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Office.Exceptions;
using SkyCD.Plugin.Metadata.Office.Models;
using SkyCD.Plugin.Metadata.Office.Parsing;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.Office.Tests;

/// <summary>
/// Coverage for the four applications the plugin added on top of Word, Excel and PowerPoint, for the legacy
/// containers each of them still stores, and for the protected inputs all seven can present.
/// </summary>
public class OfficeFamilyMetadataTests
{
    [Fact]
    public async Task WordPackageWrittenAsLegacyCompound_KeepsItsOwnValuesAndIdentifiesWord()
    {
        var document = OfficeFixtures.Doc();

        var result = await ExtractAsync(document, ".doc");

        Assert.Equal("ole", Text(result, OfficeSchema.Container));
        Assert.Equal("word", Text(result, OfficeSchema.DocumentType));
    }

    [Fact]
    public async Task WorkbookWrittenAsLegacyCompound_IsIdentifiedAsExcel()
    {
        var document = OfficeFixtures.Xls();

        var result = await ExtractAsync(document, ".xls");

        Assert.Equal("ole", Text(result, OfficeSchema.Container));
        Assert.Equal("excel", Text(result, OfficeSchema.DocumentType));
    }

    [Fact]
    public async Task PresentationWrittenAsLegacyCompound_IsIdentifiedAsPowerPoint()
    {
        var document = OfficeFixtures.Ppt();

        var result = await ExtractAsync(document, ".ppt");

        Assert.Equal("ole", Text(result, OfficeSchema.Container));
        Assert.Equal("powerpoint", Text(result, OfficeSchema.DocumentType));
    }

    [Fact]
    public async Task TemplateExtensions_ArePublishedAsTemplates()
    {
        var document = OfficeFixtures.Doc();

        var result = await ExtractAsync(document, ".dot");

        Assert.True(result.Values.Single(value => value.Key == OfficeSchema.IsTemplate).Boolean);
    }

    [Fact]
    public async Task VisioDrawing_PublishesItsPropertiesAndPageCount()
    {
        var result = await ExtractAsync(OfficeFixtures.Vsdx(pages: 2), ".vsdx");

        Assert.Equal("ooxml", Text(result, OfficeSchema.Container));
        Assert.Equal("visio", Text(result, OfficeSchema.DocumentType));
        Assert.Equal("Fixture drawing", Text(result, OfficeSchema.Title));
        Assert.Equal("A. Drafter", Text(result, OfficeSchema.Author));
        Assert.Equal(2L, Integer(result, OfficeSchema.PageCount));
        Assert.Equal("drawing", Text(result, OfficeSchema.FormatProfile));
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.IsTemplate);
    }

    [Fact]
    public async Task VisioStencil_IsPublishedAsAnAuthoringShape()
    {
        var result = await ExtractAsync(OfficeFixtures.Vssx(), ".vssx");

        Assert.Equal("visio", Text(result, OfficeSchema.DocumentType));
        Assert.Equal("Fixture stencil", Text(result, OfficeSchema.Title));
        Assert.True(result.Values.Single(value => value.Key == OfficeSchema.IsTemplate).Boolean);
    }

    [Fact]
    public async Task LegacyVisioDrawing_IsIdentifiedByItsExtensionAndReadFromItsPropertySet()
    {
        // A legacy Visio stream is not modelled here, so the extension supplies the application and the summary
        // property set supplies the document's own values.
        var document = OfficeFixtures.LegacyVisio();

        var result = await ExtractAsync(document, ".vsd");

        Assert.Equal("ole", Text(result, OfficeSchema.Container));
        Assert.Equal("visio", Text(result, OfficeSchema.DocumentType));
        Assert.Equal("Quarterly Report", Text(result, OfficeSchema.Title));
        Assert.Equal("A. Writer", Text(result, OfficeSchema.Author));
        Assert.Equal(OfficeFixtures.Created, Timestamp(result, OfficeSchema.CreatedAt));
    }

    [Fact]
    public async Task OneNoteSection_PublishesItsPagesAndTheirWindow()
    {
        var result = await ExtractAsync(OfficeFixtures.One("Page one", "Page two"), ".one");

        Assert.Equal("onenote", Text(result, OfficeSchema.Container));
        Assert.Equal("onenote", Text(result, OfficeSchema.DocumentType));
        Assert.Equal("Fixture section", Text(result, OfficeSchema.Title));
        Assert.Equal(2L, Integer(result, OfficeSchema.SheetCount));
        Assert.Equal("Page one\nPage two", Text(result, OfficeSchema.PageTitles));
        Assert.Equal(OfficeFixtures.Created, Timestamp(result, OfficeSchema.CreatedAt));

        // A section publishes the window its pages span, so the modification is the newer page write.
        Assert.Equal(OfficeFixtures.Modified.AddMinutes(1), Timestamp(result, OfficeSchema.ModifiedAt));
    }

    [Fact]
    public async Task OneNoteNotebookPackage_PublishesSectionsAndNeverAFileLabelAsTitle()
    {
        var result = await ExtractAsync(OfficeFixtures.Onepkg(), ".onepkg");

        Assert.Equal("cab", Text(result, OfficeSchema.Container));
        Assert.Equal("onenote", Text(result, OfficeSchema.DocumentType));
        Assert.Equal(1L, Integer(result, OfficeSchema.SectionCount));
        Assert.Equal("Section one", Text(result, OfficeSchema.PageTitles));
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.Title);
    }

    [Fact]
    public async Task AccessDatabase_PublishesItsTitleAndObjectCounts()
    {
        var document = OfficeFixtures.Accdb(tables: 2);

        var result = await ExtractAsync(document, ".accdb");

        Assert.Equal("jet", Text(result, OfficeSchema.Container));
        Assert.Equal("access", Text(result, OfficeSchema.DocumentType));
        Assert.Equal("Fixture database", Text(result, OfficeSchema.Title));
        Assert.Equal(2L, Integer(result, OfficeSchema.TableCount));
        Assert.NotNull(Text(result, OfficeSchema.FormatProfile));
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.QueryCount);
    }

    [Fact]
    public async Task AccessDatabaseWrittenAsMdb_IsReadAsJetToo()
    {
        var document = OfficeFixtures.Mdb(tables: 1);

        var result = await ExtractAsync(document, ".mdb");

        Assert.Equal("access", Text(result, OfficeSchema.DocumentType));
        Assert.Equal(1L, Integer(result, OfficeSchema.TableCount));
    }

    [Fact]
    public async Task ProjectPlan_PublishesItsPropertiesScheduleAndTaskCounts()
    {
        var document = OfficeFixtures.Mpp(tasks: 3);

        var result = await ExtractAsync(document, ".mpp");

        Assert.Equal("ole", Text(result, OfficeSchema.Container));
        Assert.Equal("project", Text(result, OfficeSchema.DocumentType));
        Assert.Equal("Fixture plan", Text(result, OfficeSchema.Title));
        Assert.Equal("A. Planner", Text(result, OfficeSchema.Author));
        Assert.Equal("SkyCD Ltd", Text(result, OfficeSchema.Company));
        Assert.Equal("M. Manager", Text(result, OfficeSchema.Manager));
        Assert.Equal(3L, Integer(result, OfficeSchema.TaskCount));
        Assert.Equal(3L, Integer(result, OfficeSchema.TopLevelTaskCount));
        Assert.Equal(OfficeFixtures.PlanStart, Timestamp(result, OfficeSchema.PlanStartAt));
        Assert.Equal(OfficeFixtures.PlanFinish, Timestamp(result, OfficeSchema.PlanFinishAt));
    }

    [Fact]
    public async Task ProjectTemplate_IsPublishedAsATemplate()
    {
        var result = await ExtractAsync(OfficeFixtures.Mpt(), ".mpt");

        Assert.Equal("project", Text(result, OfficeSchema.DocumentType));
        Assert.True(result.Values.Single(value => value.Key == OfficeSchema.IsTemplate).Boolean);
    }

    [Fact]
    public async Task ProjectInterchangeFile_IsReadAsTextRatherThanAsACompoundDocument()
    {
        var result = await ExtractAsync(OfficeFixtures.Mpx(tasks: 2), ".mpx");

        Assert.Equal("text", Text(result, OfficeSchema.Container));
        Assert.Equal("project", Text(result, OfficeSchema.DocumentType));
        Assert.Equal(2L, Integer(result, OfficeSchema.TaskCount));
    }

    [Fact]
    public async Task PasswordProtectedWordPackage_IsReportedAsProtectedAndLeftUnread()
    {
        var result = await ExtractAsync(OfficeFixtures.ProtectedDocx(), ".docx");

        Assert.True(result.Values.Single(value => value.Key == OfficeSchema.IsEncrypted).Boolean);
        Assert.False(string.IsNullOrWhiteSpace(Text(result, OfficeSchema.ProtectionReason)));
        Assert.DoesNotContain(result.Values, value => value.Key == OfficeSchema.Title);
    }

    [Fact]
    public async Task PasswordProtectedWorkbook_IsReportedAsProtectedAndLeftUnread()
    {
        var result = await ExtractAsync(OfficeFixtures.ProtectedXlsx(), ".xlsx");

        Assert.True(result.Values.Single(value => value.Key == OfficeSchema.IsEncrypted).Boolean);
        Assert.False(string.IsNullOrWhiteSpace(Text(result, OfficeSchema.ProtectionReason)));
    }

    [Fact]
    public async Task PasswordProtectedPresentation_IsReportedAsProtectedAndLeftUnread()
    {
        var result = await ExtractAsync(OfficeFixtures.ProtectedPptx(), ".pptx");

        Assert.True(result.Values.Single(value => value.Key == OfficeSchema.IsEncrypted).Boolean);
        Assert.False(string.IsNullOrWhiteSpace(Text(result, OfficeSchema.ProtectionReason)));
    }

    [Fact]
    public void OneNoteEncryptedCurrentRevision_IsProtectedRatherThanSilentlyReplacedByAnOlderRevision()
    {
        var protectedSection = OfficeProtection.FromDiagnostics(
            OfficeContainers.OneNote,
            ["onenote-encrypted-current-revision: the current revision is encrypted"]);

        Assert.NotNull(protectedSection);
        Assert.True(protectedSection!.IsEncrypted);
        Assert.Equal(OfficeContainers.OneNote, protectedSection.Container);
        Assert.Contains("encrypted", protectedSection.ProtectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryDiagnostics_LeaveTheReadResultAlone()
    {
        Assert.Null(OfficeProtection.FromDiagnostics(
            OfficeContainers.OneNote, ["onenote-unknown-object: an element this version does not model"]));
    }

    [Theory]
    [InlineData("the part exceeds the allowed uncompressed size", false)]
    [InlineData("the package holds no Word, Excel, PowerPoint or Visio document part", false)]
    [InlineData("the document is encrypted", true)]
    [InlineData("password required to open this workbook", true)]
    public void ProtectionIsClassifiedFromWhatTheLibraryReported(string message, bool protectedInput)
    {
        // Only a refusal that leaves the content unread is a lock. A size limit or a missing part is an ordinary
        // failure, and publishing it as protected would hide that the document was never protected at all.
        var exception = new InvalidDataException(message);

        Assert.Equal(protectedInput, OfficeProtection.IsProtection(exception));
    }

    [Fact]
    public async Task EveryApplicationFamily_ConformsToTheDeclaredSchema()
    {
        var documents = new (byte[] Document, string Extension)[]
        {
            (OfficeFixtures.Docx(OfficeFixtures.FullCore(), OfficeFixtures.FullApp()), ".docx"),
            (OfficeFixtures.Xlsx(OfficeFixtures.Core(("dc:title", "Budget"))), ".xlsx"),
            (OfficeFixtures.PptxWithSlides(3), ".pptx"),
            (OfficeFixtures.Vsdx(), ".vsdx"),
            (OfficeFixtures.One("Page one"), ".one"),
            (OfficeFixtures.Accdb(), ".accdb"),
            (OfficeFixtures.Mpp(), ".mpp"),
            (OfficeFixtures.LegacyWord(OfficeFixtures.FullSummary()), ".doc"),
            (OfficeFixtures.LegacyVisio(), ".vsd")
        };

        foreach (var (document, extension) in documents)
        {
            var result = await ExtractAsync(document, extension);

            Assert.NotEmpty(result.Values);
            foreach (var value in result.Values)
            {
                var field = OfficeSchema.Metadata.Fields
                    .Single(candidate => candidate.Key.Equals(value.Key, StringComparison.Ordinal));
                Assert.Equal(field.Type, value.Type);
            }
        }
    }

    [Fact]
    public void PluginSources_NeverResolveAPathOrWriteATemporaryFile()
    {
        // The host hands the plugin a bounded source and nothing else. A path in these files would let a
        // document choose which file the scan opens, and a temporary copy would leave document bytes on disk.
        var forbidden = new[] { "File.ReadAll", "File.OpenRead", "File.OpenWrite", "File.Create", "File.Exists",
            "File.WriteAllText", "new FileStream", "GetTempPath", "GetTempFileName", "Path.Combine", "DriveInfo" };

        var offenders = PluginSources().SelectMany(file =>
            forbidden.Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetFileName(file)}: {token}")).ToArray();

        Assert.Empty(offenders);
    }

    private static string[] PluginSources()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.Office");
            if (Directory.Exists(candidate))
            {
                return [.. Directory.EnumerateFiles(candidate, "*.cs", SearchOption.AllDirectories)
                    .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal))];
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the Microsoft Office plugin project directory.");
    }

    private static Task<MediaExtractionResult> ExtractAsync(byte[] document, string extension) =>
        new OfficeMetadataPlugin().ExtractAsync(
            new MediaItem(extension, null, document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

    private static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    private static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    private static DateTimeOffset? Timestamp(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Timestamp;
}
