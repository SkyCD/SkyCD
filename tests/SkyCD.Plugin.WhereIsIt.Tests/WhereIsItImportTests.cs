using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.WhereIsIt;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class WhereIsItImportTests
{
    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new WhereIsItPlugin()]);
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "WhereIsIt", name);

    private static async Task<WhereIsItReportDocument> ReadAsync(string name)
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath(name));
        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "whereisit-xml-report",
            Source = source,
            FileName = name
        });

        Assert.True(result.Success, $"Import failed: {result.Error}");
        return Assert.IsType<WhereIsItReportDocument>(result.Payload);
    }

    private static Dictionary<string, Dictionary<string, object?>> IndexById(WhereIsItReportDocument document)
    {
        return document.Rows.ToDictionary(
            row => Convert.ToString(row["id"], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            row => row,
            StringComparer.Ordinal);
    }

    private static string TextOf(Dictionary<string, object?> row, string key)
    {
        return Convert.ToString(row[key], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static Dictionary<string, object?> PropertiesOf(Dictionary<string, object?> row)
    {
        return Assert.IsType<Dictionary<string, object?>>(row["properties"]);
    }

    [Fact]
    public void GetFormats_RegistersWhereIsItAsReadOnly()
    {
        var service = CreateService();

        Assert.Contains(service.GetOpenFormats(),
            format => format.FormatId == "whereisit-xml-report" && format.Extensions.Contains(".xml"));
        Assert.DoesNotContain(service.GetSaveFormats(),
            format => format.FormatId == "whereisit-xml-report");
    }

    [Fact]
    public async Task CompleteImport_PreservesHierarchyAcrossDisks()
    {
        var document = await ReadAsync("complete.xml");
        var byId = IndexById(document);

        Assert.Equal(2 + 3 + 5, document.Rows.Count);
        Assert.Equal("Home Archive", document.Summary.Title);
        Assert.Equal(2, document.Summary.Volumes);
        Assert.Equal(3, document.Summary.Folders);
        Assert.Equal(5, document.Summary.Files);

        Assert.Equal("volume:Backup", TextOf(byId["folder:Backup/Documents"], "parentId"));
        Assert.Equal("folder:Backup/Documents/Projects", TextOf(byId["file:Backup/Documents/Projects/app.exe"], "parentId"));
        Assert.Equal("file:Backup/Documents/readme.txt", TextOf(byId["file:Backup/Documents/readme.txt"], "id"));
        Assert.Equal("volume:Backup", TextOf(byId["file:Backup/loose.iso"], "parentId"));
        Assert.Equal("volume:Photos", TextOf(byId["file:Photos/beach.png"], "parentId"));

        var volume = PropertiesOf(byId["volume:Backup"]);
        Assert.Equal("Hard disk", Convert.ToString(volume["DiskType"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("1", Convert.ToString(volume["DiskNumber"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Office", Convert.ToString(volume["DiskLocation"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("WhereIsIt", Convert.ToString(volume["WhereIsItSource"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CompleteImport_RetainsMetadataSizesDatesAndUnknowns()
    {
        var document = await ReadAsync("complete.xml");
        var byId = IndexById(document);

        var readme = byId["file:Backup/Documents/readme.txt"];
        Assert.Equal(512L, readme["size"]);
        var readmeProperties = PropertiesOf(readme);
        Assert.Equal("read me & more", Convert.ToString(readmeProperties["Description"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("305441741", Convert.ToString(readmeProperties["Crc32"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Work", Convert.ToString(readmeProperties["Category"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Archive", Convert.ToString(readmeProperties["Flag"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("2024-02-06", Convert.ToString(readmeProperties["DateNormalized"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("11:31:00", Convert.ToString(readmeProperties["TimeNormalized"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("abc123", Convert.ToString(readmeProperties["WhereIsIt.CHECKSUM"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("File", Convert.ToString(readmeProperties["ExportItemType"], System.Globalization.CultureInfo.InvariantCulture));

        var app = PropertiesOf(byId["file:Backup/Documents/Projects/app.exe"]);
        Assert.Equal("first line\nsecond line", Convert.ToString(app["Description"], System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(10737418240L, byId["file:Backup/loose.iso"]["size"]);
        Assert.Equal("Grüße.mp3", TextOf(byId["file:Backup/Musik/Grüße.mp3"], "name"));

        Assert.DoesNotContain(document.Rows, row => TextOf(row, "name").Contains("*** DEMO ***", StringComparison.Ordinal));
        Assert.DoesNotContain(document.Rows, row => TextOf(row, "id").Contains("weird", StringComparison.Ordinal));
        Assert.True(document.Summary.SkippedRows >= 2);
        Assert.Contains(document.Summary.Notes, note => note.Contains("CHECKSUM", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingFields_AreSkippedWithReasons()
    {
        var document = await ReadAsync("missing-fields.xml");
        var byId = IndexById(document);

        Assert.Equal(2, document.Rows.Count);
        Assert.Contains(byId.Keys, id => id.EndsWith("/kept.txt", StringComparison.Ordinal));
        Assert.Equal(1, document.Summary.SkippedRows);
        Assert.Contains(document.Summary.Notes, note => note.Contains("PATH", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BadValues_FallBackWithoutAborting()
    {
        var document = await ReadAsync("bad-values.xml");
        var byId = IndexById(document);

        Assert.Equal(2, document.Rows.Count);
        var row = IndexById(document)["file:Odd/weird.bin"];
        Assert.Equal(0L, row["size"]);
        Assert.Equal("not-a-date", TextOf(PropertiesOf(row), "Date"));
        Assert.False(PropertiesOf(row).ContainsKey("DateNormalized"));
    }

    [Fact]
    public async Task DiskKindReport_FailsWithoutPartialPublish()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("disk-kind.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "whereisit-xml-report",
            Source = source,
            FileName = "disk-kind.xml"
        }));

        Assert.Contains("incompatible kind", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DemoOnlyReport_FailsAsEmpty()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("demo-only.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "whereisit-xml-report",
            Source = source,
            FileName = "demo-only.xml"
        }));

        Assert.Contains("no catalog entries", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedXml_FailsWithMalformedDiagnostic()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("malformed.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "whereisit-xml-report",
            Source = source,
            FileName = "malformed.xml"
        }));

        Assert.Contains("malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyReport_ReturnsEmptyDiagnostic()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("empty.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "whereisit-xml-report",
            Source = source,
            FileName = "empty.xml"
        }));

        Assert.Contains("no catalog entries", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongRoot_ReturnsUnsupportedDiagnostic()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("wrong-root.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "whereisit-xml-report",
            Source = source,
            FileName = "wrong-root.xml"
        }));

        Assert.Contains("not a Where Is It", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detection_AcceptsReportsAndRejectsOthers()
    {
        var plugin = new WhereIsItPlugin();

        foreach (var name in new[] { "complete.xml", "bad-values.xml", "missing-fields.xml", "empty.xml", "demo-only.xml", "malformed.xml" })
        {
            using var stream = File.OpenRead(FixturePath(name));
            var position = stream.Position;
            Assert.True(plugin.CanHandleContent(stream, name), name);
            Assert.Equal(position, stream.Position);
        }

        using (var stream = File.OpenRead(FixturePath("wrong-root.xml")))
        {
            Assert.False(plugin.CanHandleContent(stream, "wrong-root.xml"));
        }

        // A disk-level report is still a Where Is It? report; it fails later with
        // an incompatible-kind diagnostic instead of partial results.
        using (var stream = File.OpenRead(FixturePath("disk-kind.xml")))
        {
            Assert.True(plugin.CanHandleContent(stream, "disk-kind.xml"));
        }

        using var diskExplorer = new MemoryStream(
            Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><REPORT Title=\"T\"><ITEM ItemType=\"File\"><NAME>a.txt</NAME><SIZE>1</SIZE></ITEM></REPORT>"));
        Assert.False(plugin.CanHandleContent(diskExplorer, "de.xml"));

        using var nonSeekable = new NonSeekableStream(new MemoryStream(
            Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><REPORT Title=\"T\" />")));
        Assert.False(plugin.CanHandleContent(nonSeekable, "x.xml"));
    }

    [Fact]
    public async Task CancelledImport_ThrowsOperationCanceled()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("complete.xml"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(
            new FileFormatReadRequest
            {
                FormatId = "whereisit-xml-report",
                Source = source,
                FileName = "complete.xml"
            },
            cancelled.Token));
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
