using System.IO;
using System.Linq;
using SkyCD.Plugin.FileFormat.Basenji.Exceptions;
using Xunit;
using SkyCD.Plugin.FileFormat.Basenji.Parsing;

namespace SkyCD.Plugin.FileFormat.Basenji.Tests;

public sealed class BasenjiVdbParserTests
{
    [Fact]
    public void Parse_WithSampleVdb_ReconstructsNodesAndSummary()
    {
        var bytes = BasenjiTestHelper.CreateSampleVdbBytes();
        using var stream = new MemoryStream(bytes);

        var doc = BasenjiVdbParser.Parse(stream);

        Assert.NotNull(doc);
        Assert.Equal("My Basenji Collection", doc.Summary.DatabaseName);
        Assert.Equal("Main media backup collection", doc.Summary.DatabaseDescription);
        Assert.Equal(4, doc.Summary.DatabaseVersion);
        Assert.Equal(2, doc.Summary.MediaCount);
        Assert.Equal(1, doc.Summary.DirectoryCount);
        Assert.Equal(4, doc.Summary.FileCount);

        // Find media 1
        var media1 = doc.Rows.FirstOrDefault(r => r["nodeId"]?.ToString() == "media:1");
        Assert.NotNull(media1);
        Assert.Equal("Backup CD 1", media1["name"]);
        Assert.Equal("media", media1["kind"]);
        var media1Props = media1["properties"] as System.Collections.Generic.Dictionary<string, object?>;
        Assert.NotNull(media1Props);
        Assert.Equal("Alice", media1Props["LoanedTo"]);
        Assert.Equal("Software", media1Props["Category"]);

        // Find docs folder
        var docs = doc.Rows.FirstOrDefault(r => r["nodeId"]?.ToString() == "item:1_2");
        Assert.NotNull(docs);
        Assert.Equal("Docs", docs["name"]);
        Assert.Equal("folder", docs["kind"]);
        Assert.Equal("media:1", docs["parentId"]);

        // Find report.pdf file
        var pdf = doc.Rows.FirstOrDefault(r => r["nodeId"]?.ToString() == "item:1_3");
        Assert.NotNull(pdf);
        Assert.Equal("report.pdf", pdf["name"]);
        Assert.Equal("file", pdf["kind"]);
        Assert.Equal("item:1_2", pdf["parentId"]);
        var pdfProps = pdf["properties"] as System.Collections.Generic.Dictionary<string, object?>;
        Assert.NotNull(pdfProps);
        Assert.Equal("application/pdf", pdfProps["MimeType"]);
        Assert.Equal("hash-md5-1234", pdfProps["Hash"]);

        // Find song.mp3 with metadata
        var song = doc.Rows.FirstOrDefault(r => r["nodeId"]?.ToString() == "item:1_4");
        Assert.NotNull(song);
        Assert.Equal("song.mp3", song["name"]);
        var songProps = song["properties"] as System.Collections.Generic.Dictionary<string, object?>;
        Assert.NotNull(songProps);
        Assert.Equal("The Band", songProps["Artist"]);
        Assert.Equal("Best Hits", songProps["Album"]);
        Assert.Equal("Song One", songProps["Title"]);
        Assert.Equal("2024", songProps["Year"]);

        // Find audio CD volume 2
        var media2 = doc.Rows.FirstOrDefault(r => r["nodeId"]?.ToString() == "media:2");
        Assert.NotNull(media2);
        Assert.Equal("Greatest Hits CD", media2["name"]);
        var media2Props = media2["properties"] as System.Collections.Generic.Dictionary<string, object?>;
        Assert.NotNull(media2Props);
        Assert.Equal("AudioCd", media2Props["VolumeType"]);
        Assert.Equal("3600", media2Props["Duration"]);
        Assert.Equal("2", media2Props["Tracks"]);
    }

    [Fact]
    public void Parse_WithUnicodeVdb_PreservesUnicodeCharacters()
    {
        var bytes = BasenjiTestHelper.CreateUnicodeVdbBytes();
        using var stream = new MemoryStream(bytes);

        var doc = BasenjiVdbParser.Parse(stream);

        Assert.Equal("メディア コレクション (Media Collection)", doc.Summary.DatabaseName);
        Assert.Equal("Описание архива", doc.Summary.DatabaseDescription);

        var media1 = doc.Rows.FirstOrDefault(r => r["nodeId"]?.ToString() == "media:1");
        Assert.NotNull(media1);
        Assert.Equal("メディア １ (Media 1)", media1["name"]);
        var media1Props = media1["properties"] as System.Collections.Generic.Dictionary<string, object?>;
        Assert.NotNull(media1Props);
        Assert.Equal("太郎 (Taro)", media1Props["LoanedTo"]);

        var folder = doc.Rows.FirstOrDefault(r => r["name"]?.ToString() == "ドキュメント (Documents)");
        Assert.NotNull(folder);

        var file1 = doc.Rows.FirstOrDefault(r => r["name"]?.ToString() == "отчёт_финал.txt");
        Assert.NotNull(file1);

        var file2 = doc.Rows.FirstOrDefault(r => r["name"]?.ToString() == "café_été.jpg");
        Assert.NotNull(file2);
    }

    [Fact]
    public void Parse_WithEmptyCatalog_ThrowsBasenjiEmptyCatalogException()
    {
        var bytes = BasenjiTestHelper.CreateEmptyCatalogVdbBytes();
        using var stream = new MemoryStream(bytes);

        Assert.Throws<BasenjiEmptyCatalogException>(() => BasenjiVdbParser.Parse(stream));
    }

    [Fact]
    public void Parse_WithUnsupportedVersion_ThrowsBasenjiUnsupportedVersionException()
    {
        var bytes = BasenjiTestHelper.CreateUnsupportedVersionVdbBytes(5);
        using var stream = new MemoryStream(bytes);

        Assert.Throws<BasenjiUnsupportedVersionException>(() => BasenjiVdbParser.Parse(stream));
    }

    [Fact]
    public void Parse_WithMissingTables_ThrowsBasenjiCorruptFileException()
    {
        var bytes = BasenjiTestHelper.CreateMissingTablesVdbBytes();
        using var stream = new MemoryStream(bytes);

        Assert.Throws<BasenjiCorruptFileException>(() => BasenjiVdbParser.Parse(stream));
    }

    [Fact]
    public void Parse_WithCorruptBytes_ThrowsBasenjiCorruptFileException()
    {
        using var stream = new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.Throws<BasenjiCorruptFileException>(() => BasenjiVdbParser.Parse(stream));
    }
}
