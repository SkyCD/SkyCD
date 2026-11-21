using System;
using System.Collections.Generic;
using System.IO;
using SkyCD.Plugin.CdCollect;
using SkyCD.Plugin.CdCollect.Exceptions;
using Xunit;
using SkyCD.Plugin.CdCollect.Parsing;

namespace SkyCD.Plugin.CdCollect.Tests;

public sealed class CdCollectParserTests
{
    [Fact]
    public void Parse_StandardV4Database_BuildsCorrectHierarchyAndMetadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_parse_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var dbPath = Path.Combine(tempDir, "cdcollect-sample-v4.db");

            using var stream = File.OpenRead(dbPath);
            var doc = CdCollectParser.Parse(stream);

            Assert.NotNull(doc);
            Assert.Equal(4, doc.Summary.CatalogVersion);
            Assert.Equal(2, doc.Summary.MediaCount);
            Assert.Equal(2, doc.Summary.DirectoryCount);
            Assert.Equal(4, doc.Summary.FileCount);
            Assert.Equal(2, doc.Summary.CompPluginsCount);

            // Verify Media entries
            var mediaNodes = new List<Dictionary<string, object?>>();
            foreach (var row in doc.Rows)
            {
                if (row.TryGetValue("Type", out var typeVal) && typeVal?.ToString() == "Media")
                {
                    mediaNodes.Add(row);
                }
            }

            Assert.Equal(2, mediaNodes.Count);
            Assert.Equal("Rock Albums 2024", mediaNodes[0]["Name"]);
            Assert.Equal("Backup Drive", mediaNodes[1]["Name"]);

            // Verify Rock Albums size = 8400000 + 25000000 = 33400000
            Assert.Equal(33400000L, mediaNodes[0]["Size"]);

            // Verify track01 metadata
            var track01 = Assert.Single(doc.Rows, r => r.TryGetValue("Name", out var n) && n?.ToString() == "track01.mp3");
            var props = Assert.IsAssignableFrom<IDictionary<string, object?>>(track01["Properties"]);
            Assert.Equal("Queen", props["Artist"]);
            Assert.Equal("A Night at the Opera", props["Album"]);
            Assert.Equal("Bohemian Rhapsody", props["Title"]);
            Assert.Equal("320", props["Bitrate"]);
            Assert.Equal("5:55", props["Duration"]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void Parse_LegacyV0Database_BuildsHierarchy()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_parse_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var dbPath = Path.Combine(tempDir, "cdcollect-legacy-v0.db");

            using var stream = File.OpenRead(dbPath);
            var doc = CdCollectParser.Parse(stream);

            Assert.NotNull(doc);
            Assert.Equal(0, doc.Summary.CatalogVersion);
            Assert.Equal(1, doc.Summary.MediaCount);
            Assert.Equal(1, doc.Summary.DirectoryCount);
            Assert.Equal(1, doc.Summary.FileCount);
            Assert.NotEmpty(doc.Summary.Diagnostics);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void Parse_UnicodeDatabase_PreservesUtf8Characters()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_parse_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var dbPath = Path.Combine(tempDir, "cdcollect-unicode.db");

            using var stream = File.OpenRead(dbPath);
            var doc = CdCollectParser.Parse(stream);

            Assert.NotNull(doc);
            Assert.Equal(1, doc.Summary.MediaCount);
            Assert.Contains(doc.Rows, r => r.TryGetValue("Name", out var n) && n?.ToString() == "コレクション 2024 (Collection)");
            Assert.Contains(doc.Rows, r => r.TryGetValue("Name", out var n) && n?.ToString() == "夏祭り.mp3");
            Assert.Contains(doc.Rows, r => r.TryGetValue("Name", out var n) && n?.ToString() == "Отчёт_2024.pdf");
            Assert.Contains(doc.Rows, r => r.TryGetValue("Name", out var n) && n?.ToString() == "Café_Paris.jpg");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void Parse_EmptyCatalog_ThrowsCdCollectEmptyCatalogException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_parse_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var dbPath = Path.Combine(tempDir, "cdcollect-empty.db");

            using var stream = File.OpenRead(dbPath);
            Assert.Throws<CdCollectEmptyCatalogException>(() => CdCollectParser.Parse(stream));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void Parse_NonCdCollectDatabase_ThrowsCdCollectInvalidFormatException()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "cdcollect_parse_" + Guid.NewGuid().ToString("N"));
        try
        {
            CdCollectTestHelper.EnsureFixtureFilesExist(tempDir);
            var dbPath = Path.Combine(tempDir, "non-cdcollect.db");

            using var stream = File.OpenRead(dbPath);
            Assert.Throws<CdCollectInvalidFormatException>(() => CdCollectParser.Parse(stream));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public void Parse_CorruptStream_ThrowsCdCollectCorruptDatabaseException()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        Assert.Throws<CdCollectCorruptDatabaseException>(() => CdCollectParser.Parse(stream));
    }
}
