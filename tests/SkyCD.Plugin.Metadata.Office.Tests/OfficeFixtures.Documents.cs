using System;
using System.IO;
using System.Linq;
using OfficeIMO.Access;
using OfficeIMO.Excel;
using OfficeIMO.OneNote;
using OfficeIMO.PowerPoint;
using OfficeIMO.Project;
using OfficeIMO.Visio;
using OfficeIMO.Word;

namespace SkyCD.Plugin.Metadata.Office.Tests;

internal static partial class OfficeFixtures
{
    /// <summary>
    /// A password used only to make a fixture protected. The plugin never receives it, never guesses it and
    /// never needs it, because a protected document is published as protected instead of opened.
    /// </summary>
    private const string FixturePassword = "fixture-password";

    // ---- Word ----

    /// <summary>A Word package produced by the library, optionally carrying the given metadata parts.</summary>
    public static byte[] Docx(string? core = null, string? app = null) => Word(core, app, WordFileFormat.Docx);

    /// <summary>A legacy Word binary document, written by the library as the compound file Word itself writes.</summary>
    public static byte[] Doc(string? core = null, string? app = null) => Word(core, app, WordFileFormat.Doc);

    public static byte[] Dot(string? core = null, string? app = null) => Word(core, app, WordFileFormat.Doc);

    private static byte[] Word(string? core, string? app, WordFileFormat format)
    {
        using var document = WordDocument.Create(new WordCreateOptions());
        document.AddParagraph("Fixture body");

        var bytes = new MemoryStream();
        document.Save(bytes, new WordSaveOptions { Format = format, AllowLoss = true });
        return WithProperties(bytes.ToArray(), core, app);
    }

    // ---- Excel ----

    public static byte[] Xlsx(string? core = null, string? app = null) =>
        Workbook(core, app, ExcelFileFormat.Xlsx, "Data");

    public static byte[] Xlsb(string? core = null, string? app = null) =>
        Workbook(core, app, ExcelFileFormat.Xlsb, "Data");

    /// <summary>A legacy workbook, plus a second sheet so the published sheet count is not one by construction.</summary>
    public static byte[] Xls(string? core = null, string? app = null) =>
        Workbook(core, app, ExcelFileFormat.Xls, "Data", "Notes");

    public static byte[] Xlt(string? core = null, string? app = null) =>
        Workbook(core, app, ExcelFileFormat.Xls, "Data");

    private static byte[] Workbook(string? core, string? app, ExcelFileFormat format, params string[] sheets)
    {
        using var document = ExcelDocument.Create(new ExcelCreateOptions());
        foreach (var sheet in sheets)
        {
            document.AddSheet(sheet);
        }

        var bytes = new MemoryStream();
        document.Save(bytes, new ExcelSaveOptions { Format = format, AllowLoss = true });
        return WithProperties(bytes.ToArray(), core, app);
    }

    // ---- PowerPoint ----

    public static byte[] Pptx(int slides = 2, string? core = null, string? app = null) =>
        Presentation(slides, core, app, PowerPointFileFormat.Pptx);

    public static byte[] Ppt(string? core = null, string? app = null) =>
        Presentation(2, core, app, PowerPointFileFormat.Ppt);

    public static byte[] Pot(string? core = null, string? app = null) =>
        Presentation(1, core, app, PowerPointFileFormat.Pot);

    public static byte[] Pps(string? core = null, string? app = null) =>
        Presentation(1, core, app, PowerPointFileFormat.Pps);

    private static byte[] Presentation(
        int slides,
        string? core,
        string? app,
        PowerPointFileFormat format)
    {
        using var document = PowerPointDocument.Create(new PowerPointCreateOptions());
        for (var index = 0; index < slides; index++)
        {
            document.AddSlide(PowerPointSlideLayoutType.TitleOnly, 0);
        }

        var bytes = new MemoryStream();
        document.Save(bytes, new PowerPointSaveOptions { Format = format, AllowLoss = true });
        return WithProperties(bytes.ToArray(), core, app);
    }

    /// <summary>A presentation package whose application part records the slide count the way PowerPoint does.</summary>
    public static byte[] PptxWithSlides(int slides) =>
        Pptx(slides, null, PresentationApp(slides));

    // ---- Visio ----

    public static byte[] Vsdx(int pages = 2)
    {
        using var document = VisioDocument.Create(VisioPackageType.Drawing);
        for (var index = 1; index <= pages; index++)
        {
            document.AddPage($"Page-{index}", _ => { });
        }

        document.ConfigureProperties(properties =>
        {
            properties.Title = "Fixture drawing";
            properties.Author = "A. Drafter";
        });

        return document.ToBytes();
    }

    /// <summary>A Visio stencil package, which is an authoring shape rather than a drawing.</summary>
    public static byte[] Vssx()
    {
        using var document = VisioDocument.Create(VisioPackageType.Stencil);
        document.AddPage("Stencil", _ => { });
        document.ConfigureProperties(properties => properties.Title = "Fixture stencil");
        return document.ToBytes();
    }

    /// <summary>A legacy Visio drawing: a compound file whose document stream the plugin does not model.</summary>
    public static byte[] LegacyVisio(params SummaryProperty[] summary) =>
        Compound(
            new CompoundStream("Visio 5.00 Drawing", Marker(16)),
            new CompoundStream(SummaryStream, PropertySet(summary.Length == 0 ? FullSummary() : summary)),
            new CompoundStream(DocumentSummaryStream, PropertySet(FullDocumentSummary())));

    // ---- OneNote ----

    public static byte[] One(params string[] pageTitles)
    {
        using var section = OneNoteSection.Create();
        section.Name = "Fixture section";
        foreach (var (title, index) in pageTitles.Select((title, index) => (title, index)))
        {
            section.Pages.Add(new OneNotePage
            {
                Title = title,
                CreatedUtc = Created.UtcDateTime.AddMinutes(index),
                LastModifiedUtc = Modified.UtcDateTime.AddMinutes(index)
            });
        }

        return section.ToBytes(new OneNoteSaveOptions
        {
            StorageFormat = OneNoteStorageFormat.RevisionStore,
            GeneratedTimestampUtc = Created.UtcDateTime
        });
    }

    public static byte[] Onepkg()
    {
        var notebook = new OneNoteNotebook { Name = "Fixture notebook" };
        using var section = OneNoteSection.Create();
        section.Name = "Section one";
        section.Pages.Add(new OneNotePage { Title = "Page one", CreatedUtc = Created.UtcDateTime });
        notebook.Sections.Add(section);

        return OneNotePackageWriter.Write(notebook, new OneNoteSaveOptions
        {
            GeneratedTimestampUtc = Created.UtcDateTime
        });
    }

    // ---- Access ----

    public static byte[] Accdb(int tables = 2) => Database(tables, AccessFileFormat.Accdb);

    public static byte[] Mdb(int tables = 2) => Database(tables, AccessFileFormat.Mdb);

    private static byte[] Database(int tables, AccessFileFormat format)
    {
        using var document = AccessDocument.Create(new AccessCreateOptions
        {
            Format = format,
            DatabaseTitle = "Fixture database",
            CreatedAt = Created.UtcDateTime
        });

        for (var index = 1; index <= tables; index++)
        {
            // A table the format can hold has at least one column, so the fixture gives each one a field.
            document.Tables.Add($"FixtureTable{index}").Columns.Add("Name", AccessDataType.ShortText, 32);
        }

        var bytes = new MemoryStream();
        document.Save(bytes, new AccessSaveOptions { Format = format });
        return bytes.ToArray();
    }

    // ---- Project ----

    public static byte[] Mpp(int tasks = 3) => Plan(tasks, ProjectFileFormat.Mpp14);

    public static byte[] Mpt(int tasks = 1) => Plan(tasks, ProjectFileFormat.Mpt14);

    public static byte[] Mpx(int tasks = 2) => Plan(tasks, ProjectFileFormat.Mpx4);

    private static byte[] Plan(int tasks, ProjectFileFormat format)
    {
        using var document = ProjectDocument.Create();
        document.Name = "Fixture plan";
        document.Author = "A. Planner";
        document.Company = "SkyCD Ltd";
        document.Manager = "M. Manager";
        // A native plan is scheduled against a named base calendar, so the fixture declares the standard week.
        document.Settings.Calendar = document.Calendars.AddStandardWorkingWeek("Standard");

        // A project file stores its dates without a zone, and the library refuses any other kind.
        document.Settings.StartDate = Unspecified(PlanStart);
        document.Settings.FinishDate = Unspecified(PlanFinish);
        for (var index = 1; index <= tasks; index++)
        {
            document.Tasks.Add($"Fixture task {index}");
        }

        var bytes = new MemoryStream();
        document.Save(bytes, new ProjectSaveOptions { Format = format, AllowLoss = true });
        return bytes.ToArray();
    }

    // ---- protected documents ----

    /// <summary>A Word package encrypted the way Office encrypts one: a compound wrapper around the package.</summary>
    public static byte[] ProtectedDocx()
    {
        using var document = WordDocument.Create(new WordCreateOptions());
        document.AddParagraph("Secret body");
        document.Properties.Title = "Secret";

        var bytes = new MemoryStream();
        document.Save(bytes, new WordSaveOptions { Format = WordFileFormat.Docx, Password = FixturePassword });
        return bytes.ToArray();
    }

    public static byte[] ProtectedXlsx()
    {
        using var document = ExcelDocument.Create(new ExcelCreateOptions());
        document.AddSheet("Data");
        document.Properties.Title = "Secret";

        var bytes = new MemoryStream();
        document.Save(bytes, new ExcelSaveOptions { Format = ExcelFileFormat.Xlsx, Password = FixturePassword });
        return bytes.ToArray();
    }

    public static byte[] ProtectedPptx()
    {
        using var document = PowerPointDocument.Create(new PowerPointCreateOptions());
        document.AddSlide(PowerPointSlideLayoutType.TitleOnly, 0);
        document.Properties.Title = "Secret";

        var bytes = new MemoryStream();
        document.Save(bytes, new PowerPointSaveOptions { Format = PowerPointFileFormat.Pptx, Password = FixturePassword });
        return bytes.ToArray();
    }

    // ---- document-level injection ----

    /// <summary>A Word package whose thumbnail part carries the given image.</summary>
    public static byte[] DocxWithThumbnail(byte[]? thumbnail = null) =>
        WithBinaryEntry(Docx(FullCore(), FullApp()), "docProps/thumbnail.jpeg", thumbnail ?? Jpeg(96, 64));

    /// <summary>A fixture timestamp in the zone-less form a stored date keeps it in.</summary>
    private static DateTime Unspecified(DateTimeOffset value) =>
        DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);

    private static byte[] WithProperties(byte[] package, string? core, string? app)
    {
        if (core is not null)
        {
            package = SetMetadataPart(
                package, CorePropertiesRelationship, "docProps/core.xml", CorePropertiesContentType, core);
        }

        if (app is not null)
        {
            package = SetMetadataPart(
                package, ExtendedPropertiesRelationship, "docProps/app.xml", ExtendedPropertiesContentType, app);
        }

        return package;
    }
}
