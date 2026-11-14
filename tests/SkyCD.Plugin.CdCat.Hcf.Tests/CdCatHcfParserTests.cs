using System;
using System.Collections.Generic;
using System.IO;
using SkyCD.Plugin.CdCat.Hcf;
using SkyCD.Plugin.CdCat.Hcf.Exceptions;
using Xunit;

namespace SkyCD.Plugin.CdCat.Hcf.Tests;

public sealed class CdCatHcfParserTests
{
    [Theory]
    [InlineData("123 byte", 123L)]
    [InlineData("123 bytes", 123L)]
    [InlineData("123 B", 123L)]
    [InlineData("1024", 1024L)]
    [InlineData("1.5 Kb", 1536L)]
    [InlineData("1,5 KB", 1536L)]
    [InlineData("2.0 Mb", 2097152L)]
    [InlineData("1.0 Gb", 1073741824L)]
    [InlineData("1.0 Tb", 1099511627776L)]
    [InlineData("", 0L)]
    [InlineData(null, 0L)]
    public void ParseSizeBytes_FormatsHandledCorrectly(string? input, long expectedBytes)
    {
        var result = CdCatSizeParser.ParseSizeBytes(input);
        Assert.Equal(expectedBytes, result);
    }

    [Fact]
    public void Parse_SampleV21_ParsesMediaHierarchyAndMetadata()
    {
        var gzipBytes = CdCatTestHelper.CreateGzipXml(CdCatTestHelper.SampleV21Xml);
        using var stream = new MemoryStream(gzipBytes);

        var doc = CdCatHcfParser.Parse(stream);

        Assert.NotNull(doc);
        Assert.NotNull(doc.Summary);
        Assert.Equal("2.1", doc.Summary.CatalogVersion);
        Assert.Equal(2, doc.Summary.MediaCount);
        Assert.Equal(2, doc.Summary.DirectoryCount); // Music, EmptyDir
        Assert.Equal(5, doc.Summary.FileCount); // track01, track02, movie.avi, archive.zip, photo.jpg

        // Check Media 1
        var media1 = Assert.Single(doc.Rows, r => (string?)r["nodeId"] == "media:1_Audio CD");
        Assert.Equal("media", media1["kind"]);
        Assert.Equal("Audio CD", media1["name"]);
        var media1Props = Assert.IsAssignableFrom<IDictionary<string, object?>>(media1["properties"]);
        Assert.Equal("CDRom", media1Props["DriveType"]);
        Assert.Equal("1", media1Props["VolumeNumber"]);
        Assert.Equal("Audio album backup", media1Props["Comments"]);
        Assert.Equal("Loaned to Bob", media1Props["Borrow"]);

        // Check folder
        var folder = Assert.Single(doc.Rows, r => (string?)r["nodeId"] == "folder:1_Music");
        Assert.Equal("folder", folder["kind"]);
        Assert.Equal("media:1_Audio CD", folder["parentId"]);
        var folderProps = Assert.IsAssignableFrom<IDictionary<string, object?>>(folder["properties"]);
        Assert.Equal("Collection of rock tracks", folderProps["Comments"]);

        // Check empty folder
        var emptyFolder = Assert.Single(doc.Rows, r => (string?)r["nodeId"] == "folder:1_Music/EmptyDir");
        Assert.Equal("folder", emptyFolder["kind"]);
        Assert.Equal("folder:1_Music", emptyFolder["parentId"]);
        Assert.Equal("0", emptyFolder["sizeBytes"]);

        // Check mp3 file
        var mp3File = Assert.Single(doc.Rows, r => (string?)r["nodeId"] == "file:1_Music/track01.mp3");
        Assert.Equal("file", mp3File["kind"]);
        Assert.Equal("folder:1_Music", mp3File["parentId"]);
        Assert.Equal(4718592L.ToString(), mp3File["sizeBytes"]);
        var mp3Props = Assert.IsAssignableFrom<IDictionary<string, object?>>(mp3File["properties"]);
        Assert.Equal("Best Hits", mp3Props["Album"]);
        Assert.Equal("The Band", mp3Props["Artist"]);
        Assert.Equal("Song One", mp3Props["Title"]);
        Assert.Equal("2024", mp3Props["Year"]);
        Assert.Equal("Rock", mp3Props["Genre"]);
        Assert.Equal("Original demo", mp3Props["Comments"]);

        // Check video file with parsed video/audio comment metadata
        var videoFile = Assert.Single(doc.Rows, r => (string?)r["nodeId"] == "file:2_movie.avi");
        Assert.Equal("file", videoFile["kind"]);
        var videoProps = Assert.IsAssignableFrom<IDictionary<string, object?>>(videoFile["properties"]);
        Assert.Equal("640x272", videoProps["Dimensions"]);
        Assert.Equal("1:16:09", videoProps["Duration"]);
        Assert.Contains("XVID MPEG-4", (string)videoProps["VideoFormat"]!);
        Assert.Contains("23.98 fps", (string)videoProps["VideoFormat"]!);
        Assert.Contains("ISO/MPEG Layer-3", (string)videoProps["AudioFormat"]!);

        // Check zip file with content & borrow
        var zipFile = Assert.Single(doc.Rows, r => (string?)r["nodeId"] == "file:2_archive.zip");
        var zipProps = Assert.IsAssignableFrom<IDictionary<string, object?>>(zipFile["properties"]);
        Assert.Contains("file1.txt", (string)zipProps["Content"]!);
        Assert.Equal("Borrowed by Alice", zipProps["Borrow"]);

        // Check photo with image comment
        var photoFile = Assert.Single(doc.Rows, r => (string?)r["nodeId"] == "file:2_photo.jpg");
        var photoProps = Assert.IsAssignableFrom<IDictionary<string, object?>>(photoFile["properties"]);
        Assert.Equal("751x500", photoProps["Dimensions"]);
    }

    [Fact]
    public void Parse_Unicode_PreservesMultilingualCharacters()
    {
        var gzipBytes = CdCatTestHelper.CreateGzipXml(CdCatTestHelper.UnicodeXml);
        using var stream = new MemoryStream(gzipBytes);

        var doc = CdCatHcfParser.Parse(stream);

        var media = Assert.Single(doc.Rows, r => (string?)r["kind"] == "media");
        Assert.Equal("メディア 1 (Media One)", media["name"]);

        var mp3 = Assert.Single(doc.Rows, r => (string?)r["name"] == "音楽_曲１.mp3");
        var mp3Props = Assert.IsAssignableFrom<IDictionary<string, object?>>(mp3["properties"]);
        Assert.Equal("初音ミク", mp3Props["Artist"]);
        Assert.Equal("夏祭り", mp3Props["Album"]);

        var cyrillicFile = Assert.Single(doc.Rows, r => (string?)r["name"] == "Отчёт_финал.pdf");
        var cyrillicProps = Assert.IsAssignableFrom<IDictionary<string, object?>>(cyrillicFile["properties"]);
        Assert.Equal("Годовой отчёт", cyrillicProps["Comments"]);

        var accentedDir = Assert.Single(doc.Rows, r => (string?)r["name"] == "Événements été");
        Assert.Equal("folder", accentedDir["kind"]);
    }

    [Fact]
    public void Parse_UnsupportedVersion30_ThrowsCdCatUnsupportedVersionException()
    {
        var gzipBytes = CdCatTestHelper.CreateGzipXml(CdCatTestHelper.Version30Xml);
        using var stream = new MemoryStream(gzipBytes);

        var ex = Assert.Throws<CdCatUnsupportedVersionException>(() => CdCatHcfParser.Parse(stream));
        Assert.Contains("3.0", ex.Message);
    }

    [Fact]
    public void Parse_UnsupportedVersion22_ThrowsCdCatUnsupportedVersionException()
    {
        var gzipBytes = CdCatTestHelper.CreateGzipXml(CdCatTestHelper.Version22Xml);
        using var stream = new MemoryStream(gzipBytes);

        var ex = Assert.Throws<CdCatUnsupportedVersionException>(() => CdCatHcfParser.Parse(stream));
        Assert.Contains("2.2", ex.Message);
    }

    [Fact]
    public void Parse_EmptyCatalog_ThrowsCdCatEmptyCatalogException()
    {
        var gzipBytes = CdCatTestHelper.CreateGzipXml(CdCatTestHelper.EmptyCatalogXml);
        using var stream = new MemoryStream(gzipBytes);

        Assert.Throws<CdCatEmptyCatalogException>(() => CdCatHcfParser.Parse(stream));
    }

    [Fact]
    public void Parse_MalformedXml_ThrowsCdCatMalformedXmlException()
    {
        var gzipBytes = CdCatTestHelper.CreateGzipXml("<datafile><catalog><media><unclosed>");
        using var stream = new MemoryStream(gzipBytes);

        Assert.Throws<CdCatMalformedXmlException>(() => CdCatHcfParser.Parse(stream));
    }

    [Fact]
    public void Parse_MalformedGzip_ThrowsCdCatCorruptFileException()
    {
        var corruptBytes = new byte[] { 0x1F, 0x8B, 0x08, 0x00, 0xFF, 0xFE, 0xFD };
        using var stream = new MemoryStream(corruptBytes);

        Assert.Throws<CdCatCorruptFileException>(() => CdCatHcfParser.Parse(stream));
    }

    [Fact]
    public void Parse_ExceedingDecompressionLimit_ThrowsCdCatDecompressionLimitException()
    {
        var gzipBytes = CdCatTestHelper.CreateGzipXml(CdCatTestHelper.SampleV21Xml);
        using var stream = new MemoryStream(gzipBytes);

        // Set artificially small limit of 50 bytes
        Assert.Throws<CdCatDecompressionLimitException>(() => CdCatHcfParser.Parse(stream, maxDecompressedBytes: 50));
    }
}
