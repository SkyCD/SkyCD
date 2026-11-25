using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using Xunit;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Import;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerHierarchyTests
{
    private readonly DiskCatalogMakerPlugin plugin = new();

    [Fact]
    public async Task ReadAsync_MultipleDisksAndDuplicateFilenames_PreservedDistinctly()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", "diskcatalogmaker-standard-en.txt");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "diskcatalogmaker-text",
            Source = stream,
            FileName = "diskcatalogmaker-standard-en.txt"
        });

        Assert.True(result.Success, result.Error);
        var doc = Assert.IsType<DiskCatalogMakerReportDocument>(result.Payload);

        // Verify media roots
        var mediaNodes = doc.Rows.Where(r => string.Equals(r["kind"]?.ToString(), "media", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(2, mediaNodes.Count);
        Assert.Contains(mediaNodes, m => string.Equals(m["name"]?.ToString(), "Archive Disk 1", StringComparison.Ordinal));
        Assert.Contains(mediaNodes, m => string.Equals(m["name"]?.ToString(), "Backup 2026", StringComparison.Ordinal));

        // Fully explicit folder rows: nothing synthesized, nothing skipped
        Assert.False(doc.Summary.IsPartialReport);
        Assert.Equal(12, doc.Summary.TotalRecordsRead);
        Assert.Equal(12, doc.Summary.ImportedRecordsCount);
        Assert.Equal(0, doc.Summary.SkippedRecordsCount);

        // Verify duplicate filenames in distinct media
        var readmeNodes = doc.Rows.Where(r => string.Equals(r["name"]?.ToString(), "readme.txt", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, readmeNodes.Count);

        // Every node must have a unique stable Id
        var ids = doc.Rows.Select(r => r["nodeId"]?.ToString()).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());

        // Verify each duplicate readme has a distinct parent
        var parentIds = readmeNodes.Select(r => r["parentId"]?.ToString()).Distinct().ToList();
        Assert.Equal(2, parentIds.Count);

        // Delimiter-containing names survive the round trip
        Assert.Contains(doc.Rows, r => string.Equals(r["name"]?.ToString(), "vacation, beach.jpg", StringComparison.Ordinal));
        Assert.Contains(doc.Rows, r => string.Equals(r["name"]?.ToString(), "notes\t2026.txt", StringComparison.Ordinal));

        // Verify nested folder parenting
        var selects = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "2026", StringComparison.Ordinal));
        var photos = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "Photos", StringComparison.Ordinal));
        Assert.Equal(photos["nodeId"], selects["parentId"]);
        var skiTrip = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "ski-trip.jpg", StringComparison.Ordinal));
        Assert.Equal(selects["nodeId"], skiTrip["parentId"]);

        // Verify metadata preservation on a leaf node
        var beach = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "vacation, beach.jpg", StringComparison.Ordinal));
        Assert.Equal(2516582L.ToString(System.Globalization.CultureInfo.InvariantCulture), beach["sizeBytes"]?.ToString());
        var beachProps = Assert.IsAssignableFrom<Dictionary<string, object?>>(beach["properties"]);
        Assert.Equal("JPEG image", beachProps["Kind"]);
        Assert.Equal("8BIM", beachProps["Creator"]);
        Assert.Equal("JPEG", beachProps["TypeCode"]);
        Assert.Equal("Summer trip", beachProps["Comments"]);
        Assert.Equal("Blue", beachProps["Label"]);
        Assert.Equal("2026-01-06 10:30:00", beachProps["DateModified"]);
        Assert.Equal("2025-12-20 08:15:00", beachProps["DateCreated"]);

        // Unicode names and comments are preserved
        var resume = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "résumé.pdf", StringComparison.Ordinal));
        var resumeProps = Assert.IsAssignableFrom<Dictionary<string, object?>>(resume["properties"]);
        Assert.Equal("déjà vu — naïve", resumeProps["Comments"]);
        Assert.Contains(doc.Rows, r => string.Equals(r["name"]?.ToString(), "写真.jpg", StringComparison.Ordinal));

        // Explicit folder rows keep their kind and metadata
        Assert.Equal("folder", photos["kind"]);
        var photosProps = Assert.IsAssignableFrom<Dictionary<string, object?>>(photos["properties"]);
        Assert.Equal("Family photos", photosProps["Comments"]);
    }

    [Fact]
    public async Task ReadAsync_ReorderedColumnsAndMissingAttributes_ImportsCleanly()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", "diskcatalogmaker-reordered-columns.txt");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "diskcatalogmaker-text",
            Source = stream,
            FileName = "diskcatalogmaker-reordered-columns.txt"
        });

        Assert.True(result.Success, result.Error);
        var doc = Assert.IsType<DiskCatalogMakerReportDocument>(result.Payload);

        Assert.Equal(',', doc.Summary.DetectedDelimiter);
        Assert.False(doc.Summary.IsPartialReport);
        Assert.Equal(3, doc.Summary.ImportedRecordsCount);

        // Absent attributes are simply missing from properties, not fabricated
        var vacation = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "vacation.jpg", StringComparison.Ordinal));
        var props = Assert.IsAssignableFrom<Dictionary<string, object?>>(vacation["properties"]);
        Assert.False(props.ContainsKey("Creator"));
        Assert.False(props.ContainsKey("Comments"));
        Assert.Equal("2026-01-06 10:30:00", props["DateModified"]);
    }

    [Fact]
    public async Task ReadAsync_HfsAndRelativePaths_ReconstructMacHierarchy()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", "diskcatalogmaker-hfs-paths.txt");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "diskcatalogmaker-text",
            Source = stream,
            FileName = "diskcatalogmaker-hfs-paths.txt"
        });

        Assert.True(result.Success, result.Error);
        var doc = Assert.IsType<DiskCatalogMakerReportDocument>(result.Payload);

        var mediaNodes = doc.Rows.Where(r => string.Equals(r["kind"]?.ToString(), "media", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(2, mediaNodes.Count);
        Assert.Contains(mediaNodes, m => string.Equals(m["name"]?.ToString(), "ClassicDisk", StringComparison.Ordinal));
        Assert.Contains(mediaNodes, m => string.Equals(m["name"]?.ToString(), "ModernDisk", StringComparison.Ordinal));

        // HFS colon path nests under the explicit folder row
        var finder = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "Finder", StringComparison.Ordinal));
        var systemFolder = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "System Folder", StringComparison.Ordinal));
        Assert.Equal("folder", systemFolder["kind"]);
        Assert.Equal(systemFolder["nodeId"], finder["parentId"]);
        var finderProps = Assert.IsAssignableFrom<Dictionary<string, object?>>(finder["properties"]);
        Assert.Equal("ClassicDisk", finderProps["Volume"]);

        // Relative path resolves against the Disk column
        var readme = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "readme.txt", StringComparison.Ordinal));
        var modernDisk = mediaNodes.Single(m => string.Equals(m["name"]?.ToString(), "ModernDisk", StringComparison.Ordinal));
        var documents = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "Documents", StringComparison.Ordinal));
        Assert.Equal(documents["nodeId"], readme["parentId"]);
        Assert.Equal(modernDisk["nodeId"], documents["parentId"]);
    }

    [Fact]
    public async Task ReadAsync_GermanLocalizedExport_PreservesHierarchyAndMetadata()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", "diskcatalogmaker-localized-de.txt");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "diskcatalogmaker-text",
            Source = stream,
            FileName = "diskcatalogmaker-localized-de.txt"
        });

        Assert.True(result.Success, result.Error);
        var doc = Assert.IsType<DiskCatalogMakerReportDocument>(result.Payload);

        Assert.Equal(';', doc.Summary.DetectedDelimiter);

        var media = doc.Rows.Single(r => string.Equals(r["kind"]?.ToString(), "media", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Sicherung 2026", media["name"]);

        var bilanz = doc.Rows.Single(r => string.Equals(r["name"]?.ToString(), "bilanz.pdf", StringComparison.Ordinal));
        Assert.Equal(Convert.ToInt64(Math.Round(12.5 * 1024 * 1024)), Convert.ToInt64(bilanz["sizeBytes"]));

        var notizenNodes = doc.Rows.Where(r => string.Equals(r["name"]?.ToString(), "notizen.txt", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, notizenNodes.Count);
        Assert.NotEqual(notizenNodes[0]["parentId"], notizenNodes[1]["parentId"]);
    }

    [Fact]
    public async Task ReadAsync_PartialAndInvalidRows_SynthesizesFoldersAndSummarizesSkipped()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", "diskcatalogmaker-partial-invalid.txt");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "diskcatalogmaker-text",
            Source = stream,
            FileName = "diskcatalogmaker-partial-invalid.txt"
        });

        Assert.True(result.Success, result.Error);
        var doc = Assert.IsType<DiskCatalogMakerReportDocument>(result.Payload);

        Assert.True(doc.Summary.IsPartialReport);
        Assert.Equal(4, doc.Summary.TotalRecordsRead);
        Assert.Equal(2, doc.Summary.ImportedRecordsCount);
        Assert.Equal(2, doc.Summary.SkippedRecordsCount);
        Assert.NotEmpty(doc.Summary.SkippedReasons);
        Assert.Contains(doc.Summary.UnsupportedFields, f => string.Equals(f, "Thumbnail", StringComparison.Ordinal));

        // Verify intermediate folder nodes were synthesized
        var folderNodes = doc.Rows.Where(r => string.Equals(r["kind"]?.ToString(), "folder", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Contains(folderNodes, f => string.Equals(f["name"]?.ToString(), "Deep", StringComparison.Ordinal));
        Assert.Contains(folderNodes, f => string.Equals(f["name"]?.ToString(), "Nested", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_MissingPathColumn_FailsWithDescriptiveError()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", "diskcatalogmaker-missing-path.txt");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "diskcatalogmaker-text",
            Source = stream,
            FileName = "diskcatalogmaker-missing-path.txt"
        });

        Assert.False(result.Success);
        Assert.Contains("Path", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_HeaderOnlyReport_FailsWithEmptyReportError()
    {
        var text = "Name\tPath\tSize\r\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "diskcatalogmaker-text",
            Source = stream,
            FileName = "empty.txt"
        });

        Assert.False(result.Success);
        Assert.Contains("no data rows", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_CancelledToken_ThrowsOperationCanceled()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", "diskcatalogmaker-standard-en.txt");
        await using var stream = File.OpenRead(fixturePath);
        using var cancelled = new System.Threading.CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "diskcatalogmaker-text",
            Source = stream,
            FileName = "diskcatalogmaker-standard-en.txt"
        }, cancelled.Token));
    }

    [Fact]
    public async Task ReadAsync_ProgressReporter_ReachesHundred()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskCatalogMaker", "diskcatalogmaker-standard-en.txt");
        await using var stream = File.OpenRead(fixturePath);
        var seen = new List<int>();
        var progress = new Progress<int>(seen.Add);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "diskcatalogmaker-text",
            Source = stream,
            FileName = "diskcatalogmaker-standard-en.txt",
            Progress = progress
        });

        Assert.True(result.Success, result.Error);
        Assert.Contains(100, seen);
    }

    [Fact]
    public void SplitMacPath_HandlesPosixVolumesRelativeAndHfsForms()
    {
        Assert.Equal(
            ["Archive Disk 1", "Photos", "pic.jpg"],
            DiskCatalogMakerHierarchyBuilder.SplitMacPath("/Volumes/Archive Disk 1/Photos/pic.jpg"));
        Assert.Equal(
            ["Disk", "Docs", "a.txt"],
            DiskCatalogMakerHierarchyBuilder.SplitMacPath("Disk/Docs/a.txt"));
        Assert.Equal(
            ["ClassicDisk", "System Folder", "Finder"],
            DiskCatalogMakerHierarchyBuilder.SplitMacPath("ClassicDisk:System Folder:Finder"));
        Assert.Equal(
            ["Volumes", "Docs", "a.txt"],
            DiskCatalogMakerHierarchyBuilder.SplitMacPath("Volumes/Docs/a.txt"));
        Assert.Equal(
            ["Volumes", "Docs", "a.txt"],
            DiskCatalogMakerHierarchyBuilder.SplitMacPath("Volumes:Docs:a.txt"));
        Assert.Equal(
            ["Disk", "Photos"],
            DiskCatalogMakerHierarchyBuilder.SplitMacPath("/Volumes/Disk/Photos/"));
    }

    [Theory]
    [InlineData("/Volumes/Disk/Docs/", "/Volumes/Disk/Docs/readme.txt")]
    [InlineData("Disk:Docs:", "Disk:Docs:readme.txt")]
    [InlineData(":Docs:", ":Docs:readme.txt")]
    public void Build_ExplicitDiskWithAbsoluteOrRelativePaths_DoesNotDuplicateMedia(string folderPath, string filePath)
    {
        var document = DiskCatalogMakerHierarchyBuilder.Build(
            [["Name", "Path", "Kind", "Disk"],
             ["Docs", folderPath, "", "Disk"],
             ["readme.txt", filePath, "Plain text", "Disk"]], '\t');

        var media = Assert.Single(document.Rows, row => Equals(row["kind"], "media"));
        var folder = Assert.Single(document.Rows, row => Equals(row["kind"], "folder"));
        var file = Assert.Single(document.Rows, row => Equals(row["kind"], "file"));
        Assert.Equal("Docs", folder["name"]);
        Assert.Equal(media["nodeId"], folder["parentId"]);
        Assert.Equal(folder["nodeId"], file["parentId"]);
        Assert.False(document.Summary.IsPartialReport);
    }

    [Theory]
    [InlineData("/Volumes/Disk/")]
    [InlineData("Disk:")]
    public void Build_VolumeRow_UpdatesMediaWithoutCreatingDuplicateFolder(string rootPath)
    {
        var document = DiskCatalogMakerHierarchyBuilder.Build(
            [["Name", "Path", "Size", "Kind", "Comments"],
             ["Disk", rootPath, "4096", "Folder", "Archive"],
             ["readme.txt", rootPath + "readme.txt", "1", "Plain text", ""]], '\t');

        Assert.Equal(2, document.Rows.Count);
        var media = Assert.Single(document.Rows, row => Equals(row["kind"], "media"));
        var file = Assert.Single(document.Rows, row => Equals(row["kind"], "file"));
        Assert.Equal(media["nodeId"], file["parentId"]);
        Assert.Equal("4096", media["sizeBytes"]);
        var properties = Assert.IsType<Dictionary<string, object?>>(media["properties"]);
        Assert.Equal("Archive", properties["Comments"]);
        Assert.False(document.Summary.IsPartialReport);
    }

    [Fact]
    public void Build_CaseDistinctFolders_TracksMissingFoldersSeparately()
    {
        var document = DiskCatalogMakerHierarchyBuilder.Build(
            [["Name", "Path", "Kind"],
             ["a.txt", "/Volumes/Disk/docs/a.txt", "Plain text"],
             ["Docs", "/Volumes/Disk/Docs", "Folder"]], '\t');

        Assert.Equal(2, document.Rows.Count(row => Equals(row["kind"], "folder")));
        Assert.True(document.Summary.IsPartialReport);
    }

    [Fact]
    public void Build_CaseDistinctLeafAndParentNames_PreservesParentFolder()
    {
        var document = DiskCatalogMakerHierarchyBuilder.Build(
            [["Name", "Path", "Kind"],
             ["DOCS", "/Volumes/Disk/docs", "Folder"],
             ["README", "/Volumes/Disk/docs/DOCS/readme", "Plain text"]], '\t');

        var parent = Assert.Single(document.Rows, row => Equals(row["name"], "docs"));
        var folder = Assert.Single(document.Rows, row => Equals(row["name"], "DOCS"));
        var fileParent = Assert.Single(document.Rows, row => Equals(row["name"], "readme"));
        var file = Assert.Single(document.Rows, row => Equals(row["kind"], "file"));
        Assert.Equal(parent["nodeId"], folder["parentId"]);
        Assert.Equal(folder["nodeId"], fileParent["parentId"]);
        Assert.Equal(fileParent["nodeId"], file["parentId"]);
    }

    [Fact]
    public void NormalizeDate_ParsesSlashMonthFirstAndDotDayFirst()
    {
        Assert.Equal("2026-01-06 10:30:00", DiskCatalogMakerHierarchyBuilder.NormalizeDate("01/06/2026 10:30:00"));
        Assert.Equal("2026-01-06 10:00:00", DiskCatalogMakerHierarchyBuilder.NormalizeDate("06.01.2026 10:00:00"));
        Assert.Equal("2026-01-06 10:30:00", DiskCatalogMakerHierarchyBuilder.NormalizeDate("2026-01-06 10:30:00"));
        Assert.Equal("not a date", DiskCatalogMakerHierarchyBuilder.NormalizeDate("not a date"));
    }
}
