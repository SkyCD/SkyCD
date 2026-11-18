using System;
using System.IO;
using System.Threading.Tasks;
using JetDatabaseWriter;
using JetDatabaseWriter.Enums;
using JetDatabaseWriter.Models;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.SmartCdCatalogPro.Tests;

/// <summary>
/// Builds small synthetic Jet 4 catalogs that follow the Smart CD Catalog PRO schema.
/// Nothing here is copied from a user catalog; every value is invented for the test.
/// </summary>
internal static class SmartCdCatalogProFixture
{
    public const string Password = "chpok";

    public static Task<MemoryStream> CreateCatalogAsync() => CreateAsync(Password, async writer =>
    {
        await CreateStandardSchemaAsync(writer).ConfigureAwait(false);
        await SeedStandardDataAsync(writer).ConfigureAwait(false);
    });

    public static Task<MemoryStream> CreateAsync(string password, Func<AccessWriter, Task> build)
    {
        return CreateCoreAsync(password, build);
    }

    public static async Task<MemoryStream> CreateUnrelatedJetDatabaseAsync()
    {
        return await CreateAsync(Password, async writer =>
        {
            await writer.CreateTableAsync(
                "Notes",
                [new ColumnDefinition("ID", typeof(int), 4) { IsPrimaryKey = true }, Text("Body", 255)]);
            await writer.InsertRowAsync("Notes", Row(("ID", 1), ("Body", "hello")));
        }).ConfigureAwait(false);
    }

    public static async Task CreateStandardSchemaAsync(AccessWriter writer, bool usePrimaryKeys = true)
    {
        await writer.CreateTableAsync("CDs", CdsColumns(usePrimaryKeys)).ConfigureAwait(false);
        await writer.CreateTableAsync("Paths", PathsColumns(usePrimaryKeys)).ConfigureAwait(false);
        await writer.CreateTableAsync("Elements", ElementsColumns(usePrimaryKeys)).ConfigureAwait(false);
        await writer.CreateTableAsync("Categories", ClassificationColumns()).ConfigureAwait(false);
        await writer.CreateTableAsync("Boxes", ClassificationColumns()).ConfigureAwait(false);
        await writer.CreateTableAsync("Borrows", BorrowsColumns()).ConfigureAwait(false);
        await writer.CreateTableAsync("LoanHistory", LoanHistoryColumns()).ConfigureAwait(false);
        await writer.CreateTableAsync("AccessMode", AccessModeColumns()).ConfigureAwait(false);
        await writer.CreateTableAsync("SearchWhat", [Bool("Name"), Text("Text", 255), Date("Update")]).ConfigureAwait(false);
        await writer.CreateTableAsync("ElementUpdate", [
            Int("ID"), Memo("Comment"), Text("ElementName", 255), Text("RealName", 255), Bool("IsRenamed")
        ]).ConfigureAwait(false);
    }

    public static async Task SeedStandardDataAsync(AccessWriter writer)
    {
        await writer.InsertRowAsync("CDs", Row(
            ("IDCD", 1),
            ("FileSystem", "CDFS"),
            ("DriveType", "CD-ROM"),
            ("ComputerName", "STUDIO"),
            ("Label", "MUSIC1"),
            ("Drive_Folder", "E:\\"),
            ("TotalSize", 700_000_000d),
            ("Capacity", 734_003_200d),
            ("FreeSpace", 0d),
            ("Comment", "{\\rtf1\\ansi audio catalog}"),
            ("FolderName", "MUSIC_01"),
            ("ReadOnly", false),
            ("Archive", true),
            ("Hidden", false),
            ("System", false),
            ("Compressed", false),
            ("CreationTime", new DateTime(2020, 1, 2, 3, 4, 5)),
            ("LastAccessTime", new DateTime(2020, 2, 3, 4, 5, 6)),
            ("LastWriteTime", new DateTime(2020, 3, 4, 5, 6, 7)),
            ("ContainsOfFiles", 6),
            ("ContainsOfFolders", 3),
            ("ContainsOfFilesSelf", 1),
            ("ContainsOfFoldersSelf", 1),
            ("DateScan", new DateTime(2026, 1, 2)),
            ("SN", "SN-123"),
            ("IDC", 2),
            ("IDX", 1),
            ("IDB", 1),
            ("IsLend", true),
            ("LoanStart", new DateTime(2026, 1, 5)),
            ("LoanComment", "checked out"),
            ("IsFolder", false),
            ("ScannedArchives", 1),
            ("DiskNumber", 4),
            ("DiskCase", "Blue"),
            ("Description", "Audio collection"),
            ("IsAudio", true))).ConfigureAwait(false);

        // No FolderName/Label: the display name must fall back to Drive_Folder.
        await writer.InsertRowAsync("CDs", Row(
            ("IDCD", 2),
            ("FileSystem", "NTFS"),
            ("Drive_Folder", "\\\\server\\share\\"),
            ("ReadOnly", true),
            ("Archive", false),
            ("Hidden", false),
            ("System", false),
            ("Compressed", false),
            ("IsLend", false),
            ("IsFolder", false),
            ("IsAudio", false),
            ("IDB", -1))).ConfigureAwait(false);

        await writer.InsertRowAsync("Categories", Row(
            ("ID", 1), ("Name", "Music"), ("Pedigree", "cached-pedigree"), ("Catpath", "WRONG"))).ConfigureAwait(false);
        await writer.InsertRowAsync("Categories", Row(
            ("ID", 2), ("Name", "Rock"), ("IDPARENT", 1), ("Catpath", "WRONG\\Rock"))).ConfigureAwait(false);
        await writer.InsertRowAsync("Boxes", Row(("ID", 1), ("Name", "Shelf A"))).ConfigureAwait(false);
        await writer.InsertRowAsync("Borrows", Row(
            ("ID", 1),
            ("Name", "Alice"),
            ("Address", "1 Test Street"),
            ("PhoneHome", "555-0100"),
            ("PhoneWork", "555-0101"),
            ("PhoneMobile", "555-0102"),
            ("Email", "alice@example.test"),
            ("Woman", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("LoanHistory", Row(
            ("IDHistory", 1),
            ("IDCD", 2),
            ("IDB", 1),
            ("IsLend", true),
            ("LoanStart", new DateTime(2020, 1, 1)),
            ("LoanEnd", new DateTime(2020, 2, 1)),
            ("LoanComment", "old loan"))).ConfigureAwait(false);
        await writer.InsertRowAsync("LoanHistory", Row(
            ("IDHistory", 2),
            ("IDCD", 1),
            ("IDB", 1),
            ("IsLend", true),
            ("LoanStart", new DateTime(2026, 1, 5)))).ConfigureAwait(false);

        await writer.InsertRowAsync("AccessMode", Row(
            ("FullPW", "full-secret"),
            ("ReadOnlyPW", "read-secret"),
            ("DefaultIsFull", true))).ConfigureAwait(false);

        await writer.InsertRowAsync("Paths", Row(("IDPATH", 100), ("IDCD", 1), ("Path", "E:\\"), ("IsArchive", false))).ConfigureAwait(false);
        await writer.InsertRowAsync("Paths", Row(("IDPATH", 101), ("IDCD", 1), ("Path", "E:\\Albums\\"), ("IsArchive", false))).ConfigureAwait(false);
        await writer.InsertRowAsync("Paths", Row(("IDPATH", 102), ("IDCD", 1), ("Path", "E:\\Albums\\Sub\\"), ("IsArchive", false))).ConfigureAwait(false);
        await writer.InsertRowAsync("Paths", Row(("IDPATH", 103), ("IDCD", 1), ("Path", "E:\\Albums\\Bootleg.zip\\"), ("IsArchive", true))).ConfigureAwait(false);
        await writer.InsertRowAsync("Paths", Row(("IDPATH", 110), ("IDCD", 2), ("Path", "\\\\server\\share\\"), ("IsArchive", false))).ConfigureAwait(false);
        await writer.InsertRowAsync("Paths", Row(("IDPATH", 111), ("IDCD", 2), ("Path", "\\\\server\\share\\docs\\"), ("IsArchive", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1000), ("IDPATH", 100), ("ElementName", "Albums"), ("IDPATHSELF", 101),
            ("IsFolder", true), ("IsRenamed", false), ("ReadOnly", false), ("Archive", false),
            ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false),
            ("Comment", "folder comment"), ("ContainsOfFiles", 4), ("ContainsOfFolders", 2))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1001), ("IDPATH", 101), ("ElementName", "söng ☃.mp3"),
            ("ElementSize", 9_876_543_210d), ("IsFolder", false), ("Ext", "mp3"),
            ("IsAudio", true), ("TrackN", (short)3), ("Duration", new DateTime(2026, 1, 1, 0, 3, 45)),
            ("ReadOnly", false), ("Archive", false), ("Hidden", false), ("System", false), ("Compressed", true),
            ("CreationTime", new DateTime(2021, 1, 1, 1, 1, 1)),
            ("LastAccessTime", new DateTime(2021, 2, 2, 2, 2, 2)),
            ("LastWriteTime", new DateTime(2021, 3, 3, 3, 3, 3)),
            ("Comment", "audio comment"), ("IsRenamed", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1002), ("IDPATH", 101), ("ElementName", "empty.txt"), ("ElementSize", 0d),
            ("IsFolder", false), ("Ext", "txt"), ("IsRenamed", false), ("ReadOnly", false),
            ("Archive", false), ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1003), ("IDPATH", 101), ("ElementName", "renamed.log"), ("RealName", "actual.log"),
            ("IsRenamed", true), ("ElementSize", 17d), ("IsFolder", false), ("Ext", "log"),
            ("ReadOnly", false), ("Archive", false), ("Hidden", false), ("System", false),
            ("Compressed", false), ("IsAudio", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1004), ("IDPATH", 101), ("ElementName", "notes.txt"), ("ElementSize", 512d),
            ("IsFolder", false), ("Ext", "txt"), ("IsRenamed", false), ("ReadOnly", true),
            ("Archive", true), ("Hidden", true), ("System", false), ("Compressed", false), ("IsAudio", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1005), ("IDPATH", 103), ("ElementName", "bootleg.flac"), ("ElementSize", 123d),
            ("UnpackedSize", 456d), ("ScannedArchives", 2), ("IsFolder", false), ("Ext", "flac"),
            ("IsRenamed", false), ("ReadOnly", false), ("Archive", false), ("Hidden", false),
            ("System", false), ("Compressed", false), ("IsAudio", true))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1006), ("IDPATH", 100), ("ElementName", "big.bin"), ("ElementSize", 1_099_511_627_776d),
            ("IsFolder", false), ("Ext", "bin"), ("IsRenamed", false), ("ReadOnly", false),
            ("Archive", false), ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1007), ("IDPATH", 101), ("ElementName", "Sub"), ("IDPATHSELF", 102),
            ("IsFolder", true), ("IsRenamed", false), ("ReadOnly", false), ("Archive", false),
            ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1008), ("IDPATH", 102), ("ElementName", "deep.bin"), ("ElementSize", 42d),
            ("IsFolder", false), ("Ext", "bin"), ("IsRenamed", false), ("ReadOnly", false),
            ("Archive", false), ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1100), ("IDPATH", 110), ("ElementName", "docs"), ("IDPATHSELF", 111),
            ("IsFolder", true), ("IsRenamed", false), ("ReadOnly", false), ("Archive", false),
            ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1101), ("IDPATH", 111), ("ElementName", "readme.md"), ("ElementSize", 10d),
            ("IsFolder", false), ("Ext", "md"), ("IsRenamed", false), ("ReadOnly", false),
            ("Archive", false), ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false))).ConfigureAwait(false);

        await writer.InsertRowAsync("Elements", Row(
            ("ID", 1102), ("IDPATH", 111), ("ElementName", "crème brûlée.txt"), ("ElementSize", 512d),
            ("IsFolder", false), ("Ext", "txt"), ("IsRenamed", false), ("ReadOnly", false),
            ("Archive", false), ("Hidden", false), ("System", false), ("Compressed", false), ("IsAudio", false))).ConfigureAwait(false);
    }

    public static RowValues Row(params (string Column, object? Value)[] values)
    {
        var row = RowValues.Create();
        foreach (var (column, value) in values)
        {
            if (value is not null)
            {
                row.Set(column, value);
            }
        }

        return row;
    }

    public static FileFormatReadRequest ReadRequest(Stream source, string fileName = "catalog.scdc")
    {
        return new FileFormatReadRequest
        {
            Source = source,
            FormatId = "smart-cd-catalog-pro-scdc",
            FileName = fileName,
            SourcePath = fileName
        };
    }

    private static async Task<MemoryStream> CreateCoreAsync(string password, Func<AccessWriter, Task> build)
    {
        var path = Path.Combine(Path.GetTempPath(), $"scdc-fixture-{Guid.NewGuid():N}.scdc");
        try
        {
            var options = new AccessWriterOptions
            {
                Password = password.AsMemory(),
                UseLockFile = false
            };

            await using (var writer = await AccessWriter.CreateDatabaseAsync(
                             path,
                             DatabaseFormat.Jet4Mdb,
                             options))
            {
                await build(writer).ConfigureAwait(false);
            }

            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            return new MemoryStream(bytes, writable: false);
        }
        finally
        {
            TryDelete(path);
            TryDelete(path + ".jdw-journal");
            TryDelete(Path.ChangeExtension(path, ".ldb"));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    public static ColumnDefinition[] CdsColumns(bool usePrimaryKeys) =>
    [
        Int("IDCD", usePrimaryKeys), Text("FileSystem", 50), Text("DriveType", 50),
        Text("ComputerName", 255), Text("Label", 50), Text("Drive_Folder", 255),
        Double("TotalSize"), Double("Capacity"), Double("FreeSpace"), Memo("Comment"),
        Text("FolderName", 255), Bool("ReadOnly", nullable: false), Bool("Archive", nullable: false),
        Bool("Hidden", nullable: false), Bool("System", nullable: false), Bool("Compressed", nullable: false),
        Date("CreationTime"), Date("LastAccessTime"), Date("LastWriteTime"),
        Int("ContainsOfFiles"), Int("ContainsOfFolders"), Int("ContainsOfFilesSelf"), Int("ContainsOfFoldersSelf"),
        Date("DateScan"), Text("SN", 50), Int("IDC"), Int("IDX"), Int("IDB", defaultValue: -1),
        Bool("IsLend", nullable: false), Date("LoanStart"), Memo("LoanComment"),
        Bool("IsFolder", nullable: false), Int("ScannedArchives"), Int("DiskNumber"), Text("DiskCase", 50),
        Memo("Description"), Bool("IsAudio", nullable: false)
    ];

    public static ColumnDefinition[] PathsColumns(bool usePrimaryKeys) =>
    [
        Int("IDPATH", usePrimaryKeys), Int("IDCD"), Memo("Path"), Bool("IsArchive", nullable: false)
    ];

    public static ColumnDefinition[] ElementsColumns(bool usePrimaryKeys) =>
    [
        Int("ID", usePrimaryKeys), Int("IDPATH"), Text("ElementName", 255), Memo("Comment"),
        Double("ElementSize"), Int("IDPATHSELF"), Bool("ReadOnly", nullable: false),
        Bool("Archive", nullable: false), Bool("Hidden", nullable: false), Bool("System", nullable: false),
        Bool("Compressed", nullable: false), Int("ContainsOfFiles"), Int("ContainsOfFolders"),
        Int("ContainsOfFilesSelf"), Int("ContainsOfFoldersSelf"), Date("CreationTime"),
        Date("LastAccessTime"), Date("LastWriteTime"), Bool("IsFolder", nullable: false),
        Text("Ext", 255), Double("UnpackedSize"), Int("ScannedArchives"), Date("Duration"),
        Memo("Description"), Bool("IsAudio", nullable: false), Short("TrackN"), Text("RealName", 255),
        Bool("IsRenamed", nullable: false)
    ];

    private static ColumnDefinition[] ClassificationColumns() =>
    [
        Int("ID", primaryKey: true), Text("Name", 255), Int("IDPARENT"), Memo("Pedigree"),
        Memo("Comment"), Memo("Catpath")
    ];

    private static ColumnDefinition[] BorrowsColumns() =>
    [
        Int("ID", primaryKey: true), Text("Name", 255), Int("IDPARENT"), Memo("Pedigree"),
        Memo("Comment"), Memo("Address"), Text("PhoneHome", 255), Text("PhoneWork", 255),
        Text("PhoneMobile", 255), Text("Email", 255), Bool("Woman", nullable: false), Memo("Catpath")
    ];

    private static ColumnDefinition[] LoanHistoryColumns() =>
    [
        Int("IDHistory", primaryKey: true), Int("IDCD"), Int("IDB"), Bool("IsLend", nullable: false),
        Date("LoanStart"), Date("LoanEnd"), Memo("LoanComment")
    ];

    private static ColumnDefinition[] AccessModeColumns() =>
    [
        Text("FullPW", 255), Text("ReadOnlyPW", 255), Bool("DefaultIsFull", nullable: false)
    ];

    private static ColumnDefinition Int(string name, bool primaryKey = false, int? defaultValue = null, bool nullable = true)
    {
        return new ColumnDefinition(name, typeof(int), 4)
        {
            IsPrimaryKey = primaryKey,
            IsNullable = !primaryKey && nullable,
            DefaultValue = defaultValue
        };
    }

    private static ColumnDefinition Short(string name) => new(name, typeof(short), 2) { IsNullable = true };

    private static ColumnDefinition Double(string name) => new(name, typeof(double), 8) { IsNullable = true };

    private static ColumnDefinition Date(string name) => new(name, typeof(DateTime), 8) { IsNullable = true };

    private static ColumnDefinition Bool(string name, bool nullable = true) => new(name, typeof(bool), 1) { IsNullable = nullable };

    private static ColumnDefinition Text(string name, int length) => new(name, typeof(string), length) { IsNullable = true };

    private static ColumnDefinition Memo(string name) => new(name, typeof(string), 0) { IsNullable = true };
}
