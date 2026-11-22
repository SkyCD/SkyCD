using System;
using System.IO;
using System.Linq;
using SkyCD.Plugin.FileFormat.Librer.Exceptions;
using Xunit;
using SkyCD.Plugin.FileFormat.Librer.Models;
using SkyCD.Plugin.FileFormat.Librer.Parsing;

namespace SkyCD.Plugin.FileFormat.Librer.Tests;

public sealed class LibrerParserTests
{
    private readonly LibrerParser _parser = new();

    [Fact]
    public void LibrerFlags_DecodesBitmaskCorrectly()
    {
        // 528 = 512 (IsDir) + 16 (HasFiles)
        var folderFlags = new LibrerFlags(528);
        Assert.True(folderFlags.IsDir);
        Assert.False(folderFlags.IsFile);
        Assert.True(folderFlags.HasFiles);
        Assert.False(folderFlags.HasCd);

        // 288 = 256 (IsFile) + 32 (HasCd)
        var fileWithCdFlags = new LibrerFlags(288);
        Assert.False(fileWithCdFlags.IsDir);
        Assert.True(fileWithCdFlags.IsFile);
        Assert.False(fileWithCdFlags.HasFiles);
        Assert.True(fileWithCdFlags.HasCd);

        // Symlink + File = 128 + 256 = 384
        var symlinkFlags = new LibrerFlags(384);
        Assert.True(symlinkFlags.IsSymlink);
        Assert.True(symlinkFlags.IsFile);
    }

    [Fact]
    public void Parse_ValidLibrerFile_ProducesExpectedHierarchyAndMetadata()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"librer_parse_{Guid.NewGuid():N}.librer_data");
        try
        {
            LibrerTestHelper.CreateSampleLibrerCatalog(tempFile);
            using var stream = File.OpenRead(tempFile);

            var report = _parser.Parse(stream);

            Assert.NotNull(report);
            Assert.Equal(6, report.Count); // 1 Media + 2 Folders + 3 Files

            // Check summary
            Assert.Equal("ArchiveDisk2024", report.Summary.CatalogLabel);
            Assert.Equal("/media/backup", report.Summary.ScanPath);
            Assert.Equal("Backup of photos and documents", report.Summary.Comments);
            Assert.Equal("main-workstation", report.Summary.CreationHost);
            Assert.Equal("Linux 6.8.0", report.Summary.CreationOS);
            Assert.Equal(3, report.Summary.TotalFiles);
            Assert.Equal(2, report.Summary.TotalFolders);
            Assert.Equal(1550000L, report.Summary.TotalSizeBytes);
            Assert.Equal(50000000000L, report.Summary.FreeSizeBytes);
            Assert.Equal(2, report.Summary.CustomDataCount);

            // Check Media row
            var mediaRow = report[0];
            Assert.Equal("Media", mediaRow["Type"]);
            Assert.Equal("ArchiveDisk2024", mediaRow["Name"]);
            Assert.Null(mediaRow["ParentId"]);
            Assert.Equal(1550000L, mediaRow["Size"]);

            // Check Documents folder row
            var docsRow = report.FirstOrDefault(r => r["Name"]?.ToString() == "Documents");
            Assert.NotNull(docsRow);
            Assert.Equal("Folder", docsRow["Type"]);
            Assert.Equal(mediaRow["Id"], docsRow["ParentId"]);

            // Check report.pdf row with custom data
            var reportPdfRow = report.FirstOrDefault(r => r["Name"]?.ToString() == "report.pdf");
            Assert.NotNull(reportPdfRow);
            Assert.Equal("File", reportPdfRow["Type"]);
            Assert.Equal(500000L, reportPdfRow["Size"]);
            Assert.Equal(docsRow["Id"], reportPdfRow["ParentId"]);

            var pdfProps = Assert.IsAssignableFrom<System.Collections.Generic.Dictionary<string, object?>>(reportPdfRow["Properties"]);
            Assert.True(pdfProps.ContainsKey("CustomData"));
            Assert.Equal("PDF Document / 12 pages", pdfProps["CustomData"]);

            // Check sunset.jpg row with custom data
            var sunsetRow = report.FirstOrDefault(r => r["Name"]?.ToString() == "sunset.jpg");
            Assert.NotNull(sunsetRow);
            Assert.Equal("File", sunsetRow["Type"]);
            Assert.Equal(1000000L, sunsetRow["Size"]);

            var sunsetProps = Assert.IsAssignableFrom<System.Collections.Generic.Dictionary<string, object?>>(sunsetRow["Properties"]);
            Assert.True(sunsetProps.ContainsKey("CustomData"));
            Assert.Equal("EXIF: ISO 100, f/2.8, 1/500s", sunsetProps["CustomData"]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Parse_InvalidZipArchive_ThrowsLibrerInvalidArchiveException()
    {
        using var stream = new MemoryStream("not a zip"u8.ToArray());

        Assert.Throws<LibrerInvalidArchiveException>(() => _parser.Parse(stream));
    }

    [Fact]
    public void Parse_MissingHeader_ThrowsLibrerMissingHeaderException()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("filenames");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("dummy");
        }
        memoryStream.Position = 0;

        Assert.Throws<LibrerMissingHeaderException>(() => _parser.Parse(memoryStream));
    }

    [Fact]
    public void Parse_EmptyFilestructure_ThrowsLibrerEmptyCatalogException()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"librer_empty_{Guid.NewGuid():N}.librer_data");
        try
        {
            LibrerTestHelper.CreateEmptyFilestructureCatalog(tempFile);
            using var stream = File.OpenRead(tempFile);

            Assert.Throws<LibrerEmptyCatalogException>(() => _parser.Parse(stream));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
