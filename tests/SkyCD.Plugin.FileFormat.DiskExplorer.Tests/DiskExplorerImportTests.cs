using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskExplorer;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.FileFormat.DiskExplorer.Models;
using SkyCD.Plugin.FileFormat.DiskExplorer.Parsing;

namespace SkyCD.Plugin.Host.Tests;

public class DiskExplorerImportTests
{
    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new DiskExplorerPlugin()]);
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "DiskExplorer", name);

    private static async Task<DiskExplorerReportDocument> ReadAsync(string name)
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath(name));
        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-xml-report",
            Source = source,
            FileName = name
        });

        Assert.True(result.Success, $"Import failed: {result.Error}");
        return Assert.IsType<DiskExplorerReportDocument>(result.Payload);
    }

    private static Dictionary<string, Dictionary<string, object?>> IndexById(DiskExplorerReportDocument document)
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
    public void GetFormats_RegistersDiskExplorerAsReadableAndWritable()
    {
        var service = CreateService();

        Assert.Contains(service.GetOpenFormats(),
            format => format.FormatId == "disk-explorer-xml-report" && format.Extensions.Contains(".xml"));
        Assert.Contains(service.GetSaveFormats(),
            format => format.FormatId == "disk-explorer-xml-report" && format.Extensions.Contains(".xml"));
    }

    [Fact]
    public async Task CompleteImport_PreservesHierarchyTypesAndCounts()
    {
        var document = await ReadAsync("complete.xml");
        var byId = IndexById(document);

        Assert.Equal(2 + 3 + 7, document.Rows.Count);
        Assert.Equal("Home Archive", document.Summary.Title);
        Assert.Equal(2, document.Summary.Volumes);
        Assert.Equal(3, document.Summary.Folders);
        Assert.Equal(7, document.Summary.Files);

        Assert.Equal("volume:Backup 2024", TextOf(byId["folder:Backup 2024/Documents"], "parentId"));
        Assert.Equal("folder:Backup 2024/Documents/Projects",
            TextOf(byId["file:Backup 2024/Documents/Projects/readme.txt"], "parentId"));
        Assert.Equal("media", TextOf(byId["volume:Photos"], "type"));
        Assert.Equal("folder", TextOf(byId["folder:Backup 2024/Musik"], "type"));
        Assert.Equal("file", TextOf(byId["file:Backup 2024/Documents/Projects/app.exe"], "type"));

        Assert.Equal(512L, byId["file:Backup 2024/Documents/Projects/readme.txt"]["size"]);
        Assert.Equal(10737418240L, byId["file:Backup 2024/loose.iso"]["size"]);
    }

    [Fact]
    public async Task CompleteImport_RetainsMetadataDatesAndUnknowns()
    {
        var document = await ReadAsync("complete.xml");
        var byId = IndexById(document);

        var readme = PropertiesOf(byId["file:Backup 2024/Documents/Projects/readme.txt"]);
        Assert.Equal("first line\nsecond line", Convert.ToString(readme["Description"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Text File", Convert.ToString(readme["FormatType"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("2024-02-06", Convert.ToString(readme["Date"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("2024-02-06", Convert.ToString(readme["DateNormalized"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("11:31:00", Convert.ToString(readme["Time"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("abc123", Convert.ToString(readme["DiskExplorer.CHECKSUM"], System.Globalization.CultureInfo.InvariantCulture));

        var volume = PropertiesOf(byId["volume:Backup 2024"]);
        Assert.Equal("Fixed", Convert.ToString(volume["FormatType"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("2024-03-10", Convert.ToString(volume["DateNormalized"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("14:22:31", Convert.ToString(volume["TimeNormalized"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("keep-me", Convert.ToString(volume["DiskExplorer.ITEM.Custom"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Volume", Convert.ToString(volume["ExportItemType"], System.Globalization.CultureInfo.InvariantCulture));

        var sunset = PropertiesOf(byId["file:Backup 2024/Documents/sunset.jpg"]);
        Assert.Equal("true", Convert.ToString(sunset["HasThumbnail"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(sunset.ContainsKey("Thumbnail"));
        Assert.False(sunset.ContainsKey("DiskExplorer.Thumbnail"));

        var unicode = byId["file:Backup 2024/Musik/Grüße.mp3"];
        Assert.Equal("Grüße.mp3", TextOf(unicode, "name"));

        Assert.Contains(document.Summary.Notes,
            note => note.Contains("ITEM@Custom", StringComparison.Ordinal));
        Assert.Contains(document.Summary.Notes,
            note => note.Contains("Thumbnail", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MinimalImport_HandlesSelectiveColumnsAndMissingItemType()
    {
        var document = await ReadAsync("minimal.xml");
        var byId = IndexById(document);

        Assert.Equal(3, document.Rows.Count);
        var note = byId["file:Disc A/Docs/note.txt"];
        Assert.Equal("folder:Disc A/Docs", TextOf(note, "parentId"));
        Assert.Equal("file", TextOf(note, "type"));
        Assert.Equal(0L, note["size"]);
    }

    [Fact]
    public async Task MaliciousInput_IsRejectedWithTypedDiagnostics()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("malicious.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-xml-report",
            Source = source,
            FileName = "malicious.xml"
        }));

        Assert.Contains("not a Disk Explorer", exception.Message, StringComparison.OrdinalIgnoreCase);

        using var direct = File.OpenRead(FixturePath("malicious.xml"));
        Assert.Throws<SkyCD.Plugin.FileFormat.DiskExplorer.Exceptions.DiskExplorerMalformedDocumentException>(
            () => DiskExplorerParser.Parse(direct, CancellationToken.None));
    }

    [Fact]
    public async Task DeepNesting_IsRejectedWithBoundedDiagnostic()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("deep.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-xml-report",
            Source = source,
            FileName = "deep.xml"
        }));

        Assert.Contains("depth", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidNumbers_AreRejected()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("bad-number.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-xml-report",
            Source = source,
            FileName = "bad-number.xml"
        }));

        Assert.Contains("unsigned decimal", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyReport_ReturnsEmptyDiagnostic()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("empty.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-xml-report",
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
            FormatId = "disk-explorer-xml-report",
            Source = source,
            FileName = "wrong-root.xml"
        }));

        Assert.Contains("not a Disk Explorer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detection_AcceptsReportsAndRejectsOthers()
    {
        var plugin = new DiskExplorerPlugin();

        foreach (var name in new[] { "complete.xml", "minimal.xml", "empty.xml", "bad-number.xml" })
        {
            using var stream = File.OpenRead(FixturePath(name));
            var position = stream.Position;
            Assert.True(plugin.CanHandleContent(stream, name), name);
            Assert.Equal(position, stream.Position);
        }

        foreach (var name in new[] { "wrong-root.xml", "malicious.xml", "whereisit-shaped.xml" })
        {
            using var stream = File.OpenRead(FixturePath(name));
            Assert.False(plugin.CanHandleContent(stream, name), name);
        }

        using var wincatalog = new MemoryStream(
            Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><WinCatalog version=\"2024\" />"));
        Assert.False(plugin.CanHandleContent(wincatalog, "other.xml"));

        using var nonSeekable = new NonSeekableStream(new MemoryStream(
            Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><REPORT Title=\"T\" />")));
        Assert.False(plugin.CanHandleContent(nonSeekable, "x.xml"));
    }

    [Fact]
    public async Task RoundTrip_PreservesHierarchyAndMetadata()
    {
        var service = CreateService();
        var document = await ReadAsync("complete.xml");

        await using var target = new MemoryStream();
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "disk-explorer-xml-report",
            Target = target,
            FileName = "roundtrip.xml",
            Payload = document.Rows
        });
        Assert.True(writeResult.Success, $"Export failed: {writeResult.Error}");

        using var reread = new MemoryStream(target.ToArray());
        var readResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-xml-report",
            Source = reread,
            FileName = "roundtrip.xml"
        });
        Assert.True(readResult.Success, $"Reimport failed: {readResult.Error}");
        var roundTripped = Assert.IsType<DiskExplorerReportDocument>(readResult.Payload);

        Assert.Equal(document.Rows.Count, roundTripped.Rows.Count);
        var original = IndexById(document);
        var rereadIndex = IndexById(roundTripped);
        foreach (var (id, row) in original)
        {
            Assert.True(rereadIndex.TryGetValue(id, out var other), $"Missing {id} after round trip");
            Assert.Equal(TextOf(row, "name"), TextOf(other, "name"));
            Assert.Equal(TextOf(row, "parentId"), TextOf(other, "parentId"));
            Assert.Equal(TextOf(row, "type"), TextOf(other, "type"));
            Assert.Equal(row["size"], other["size"]);
        }
    }

    [Fact]
    public async Task Export_EscapesTextAndSplitsCDataMarkers()
    {
        var service = CreateService();
        var rows = new List<Dictionary<string, object?>>
        {
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "v",
                ["name"] = "V <&>",
                ["parentId"] = null,
                ["type"] = "media",
                ["size"] = 0L,
                ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            },
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = "f",
                ["name"] = "a\"b",
                ["parentId"] = "v",
                ["type"] = "file",
                ["size"] = 1L,
                ["properties"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Description"] = "x]]>y"
                }
            }
        };

        await using var target = new MemoryStream();
        var writeResult = await service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "disk-explorer-xml-report",
            Target = target,
            FileName = "escaped.xml",
            Payload = rows
        });

        Assert.True(writeResult.Success, $"Export failed: {writeResult.Error}");
        var xml = Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("V &lt;&amp;&gt;", xml, StringComparison.Ordinal);
        Assert.Contains(">a\"b<", xml, StringComparison.Ordinal);
        Assert.Contains("]]><![CDATA[", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("x]]>y", xml, StringComparison.Ordinal);
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
                FormatId = "disk-explorer-xml-report",
                Source = source,
                FileName = "complete.xml"
            },
            cancelled.Token));
    }

    [Fact]
    public async Task LargeImport_HonorsCancellation()
    {
        var service = CreateService();
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?><REPORT Title=\"Big\">");
        for (var i = 0; i < 200000; i++)
        {
            builder.Append("<ITEM ItemType=\"File\"><NAME>file").Append(i).Append(".txt</NAME><SIZE>")
                .Append(i).Append("</SIZE></ITEM>");
        }

        builder.Append("</REPORT>");
        using var cancellation = new CancellationTokenSource();
        using var source = new CancelDuringReadStream(Encoding.UTF8.GetBytes(builder.ToString()), cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(
            new FileFormatReadRequest
            {
                FormatId = "disk-explorer-xml-report",
                Source = source,
                FileName = "big.xml"
            },
            cancellation.Token));
        Assert.True(source.BytesRead >= 32768);
        Assert.True(source.BytesRead < source.Length);
    }

    private sealed class CancelDuringReadStream(byte[] data, CancellationTokenSource cancellation) : MemoryStream(data)
    {
        public override bool CanSeek => false;

        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            CancelAfterRead(read);
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            CancelAfterRead(read);
            return read;
        }

        private void CancelAfterRead(int count)
        {
            BytesRead += count;
            if (BytesRead >= 32768)
            {
                cancellation.Cancel();
            }
        }
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
