using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.DiskExplorer.Html;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.DiskExplorer.Html.Models;

namespace SkyCD.Plugin.Host.Tests;

public class DiskExplorerHtmlTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(Path.GetTempPath(), "dex-html-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new DiskExplorerHtmlPlugin()]);
    }

    private static string FixturePath(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "Fixtures", "Html", .. parts]);

    private static async Task<DiskExplorerHtmlReportDocument> ReadAsync(string sourcePath)
    {
        var service = CreateService();
        await using var source = File.OpenRead(sourcePath);
        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-html-report",
            Source = source,
            FileName = Path.GetFileName(sourcePath),
            SourcePath = sourcePath
        });

        Assert.True(result.Success, $"Import failed: {result.Error}");
        return Assert.IsType<DiskExplorerHtmlReportDocument>(result.Payload);
    }

    private static Dictionary<string, Dictionary<string, object?>> IndexById(DiskExplorerHtmlReportDocument document)
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
    public void GetFormats_RegistersHtmlReportAsReadableAndWritable()
    {
        var service = CreateService();

        Assert.Contains(service.GetOpenFormats(),
            format => format.FormatId == "disk-explorer-html-report"
                && format.Extensions.Contains(".html")
                && format.Extensions.Contains(".htm"));
        Assert.Contains(service.GetSaveFormats(),
            format => format.FormatId == "disk-explorer-html-report");
    }

    [Fact]
    public async Task CompleteBundle_ReconstructsHierarchyAndMetadata()
    {
        var document = await ReadAsync(FixturePath("complete", "index.html"));
        var byId = IndexById(document);

        Assert.Equal(12, document.Rows.Count);
        Assert.Equal(2, document.Summary.Volumes);
        Assert.Equal(3, document.Summary.Folders);
        Assert.Equal(7, document.Summary.Files);
        Assert.Equal(6, document.Summary.PagesVisited);
        Assert.True(document.Summary.Partial);

        Assert.Equal("volume:Backup 2024", TextOf(byId["folder:Backup 2024/Documents"], "parentId"));
        Assert.Equal("folder:Backup 2024/Documents/Projects",
            TextOf(byId["file:Backup 2024/Documents/Projects/app.exe"], "parentId"));
        Assert.Equal("volume:Backup 2024",
            TextOf(byId["file:Backup 2024/readme.txt"], "parentId"));

        var volume = PropertiesOf(byId["volume:Backup 2024"]);
        Assert.Equal("2024-03-10", Convert.ToString(volume["Date"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("2024-03-10", Convert.ToString(volume["DateNormalized"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("14:22:31", Convert.ToString(volume["TimeNormalized"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Fixed", Convert.ToString(volume["FormatType"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("keep-me", Convert.ToString(volume["DiskExplorerHtml.custom"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("true", Convert.ToString(volume["HasThumbnail"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("index.html", Convert.ToString(volume["HtmlPage"], System.Globalization.CultureInfo.InvariantCulture));

        var readme = PropertiesOf(byId["file:Backup 2024/readme.txt"]);
        Assert.Equal("read me & more", Convert.ToString(readme["Description"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Text File", Convert.ToString(readme["FormatType"], System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(10737418240L, byId["file:Backup 2024/loose.iso"]["size"]);
        Assert.Equal("Grüße.mp3", TextOf(byId["file:Backup 2024/Musik/Grüße.mp3"], "name"));
        Assert.Equal("summer", Convert.ToString(PropertiesOf(byId["file:Photos/beach.png"])["Description"], System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(1, document.Summary.MissingAssets);
        Assert.True(document.Summary.Partial);
    }

    [Fact]
    public async Task LegacyWindows1252Bundle_DecodesEntitiesAndCharset()
    {
        var document = await ReadAsync(FixturePath("legacy1252", "index.html"));

        Assert.Contains(document.Rows, row => TextOf(row, "name") == "Alte Bäckups");
        Assert.Contains(document.Rows, row => TextOf(row, "name") == "Sicherung");
        Assert.Contains(document.Rows, row => TextOf(row, "name") == "büro.txt");
        Assert.Equal("windows-1252", document.Summary.EncodingName);
    }

    [Fact]
    public async Task PartialBundle_WarnsAboutMissingPagesAndAssets()
    {
        var document = await ReadAsync(FixturePath("partial", "index.html"));

        Assert.True(document.Summary.Partial);
        Assert.Equal(1, document.Summary.MissingPages);
        Assert.True(document.Summary.MissingAssets >= 1);
        Assert.Contains(document.Summary.Notes, note => note.Contains("p-missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CyclicBundle_TerminatesWithCycleWarning()
    {
        var document = await ReadAsync(FixturePath("cyclic", "index.html"));

        Assert.True(document.Summary.CyclesDetected >= 1);
        Assert.NotEmpty(document.Rows);
    }

    [Fact]
    public async Task MaliciousBundle_SkipsUnsafeLinksWithoutFetching()
    {
        var document = await ReadAsync(FixturePath("malicious", "index.html"));

        Assert.True(document.Summary.SkippedExternalLinks >= 3);
        Assert.True(document.Summary.SkippedUnsafeLinks >= 3);
        Assert.True(document.Summary.Partial);
        Assert.NotEmpty(document.Rows);
    }

    [Fact]
    public async Task EmptyReport_ReturnsEmptyDiagnostic()
    {
        var service = CreateService();
        var path = FixturePath("empty.html");
        await using var source = File.OpenRead(path);
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-html-report",
            Source = source,
            FileName = "empty.html",
            SourcePath = path
        }));

        Assert.Contains("no catalog entries", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongDocument_ReturnsUnsupportedDiagnostic()
    {
        var service = CreateService();
        var path = FixturePath("wrong.html");
        await using var source = File.OpenRead(path);
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-html-report",
            Source = source,
            FileName = "wrong.html",
            SourcePath = path
        }));

        Assert.Contains("not a Disk Explorer", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detection_AcceptsReportsAndRejectsOthers()
    {
        var plugin = new DiskExplorerHtmlPlugin();

        foreach (var relative in new[] { "complete/index.html", "legacy1252/index.html", "partial/index.html", "empty.html" })
        {
            var path = FixturePath(relative.Split('/'));
            using var stream = File.OpenRead(path);
            var position = stream.Position;
            Assert.True(plugin.CanHandleContent(stream, Path.GetFileName(path)), relative);
            Assert.Equal(position, stream.Position);
        }

        using (var stream = File.OpenRead(FixturePath("wrong.html")))
        {
            Assert.False(plugin.CanHandleContent(stream, "wrong.html"));
        }

        using (var nonSeekable = new NonSeekableStream(new MemoryStream(Encoding.UTF8.GetBytes("<html><body>x</body></html>"))))
        {
            Assert.False(plugin.CanHandleContent(nonSeekable, "x.html"));
        }
    }

    [Fact]
    public async Task SinglePageWithoutBundle_ImportsAsPartial()
    {
        var service = CreateService();
        var path = FixturePath("complete", "index", "vol-photos.html");
        await using var source = File.OpenRead(path);
        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-html-report",
            Source = source,
            FileName = "vol-photos.html",
            SourcePath = null
        });

        Assert.True(result.Success, $"Import failed: {result.Error}");
        var document = Assert.IsType<DiskExplorerHtmlReportDocument>(result.Payload);
        Assert.True(document.Summary.Partial);
        Assert.NotEmpty(document.Rows);
    }

    [Fact]
    public async Task RoundTrip_PreservesHierarchyAndFields()
    {
        var service = CreateService();
        var document = await ReadAsync(FixturePath("complete", "index.html"));
        Directory.CreateDirectory(workDirectory);
        var targetPath = Path.Combine(workDirectory, "report.html");

        await using (var target = File.Create(targetPath))
        {
            var writeResult = await service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "disk-explorer-html-report",
                Target = target,
                FileName = "report.html",
                Payload = new DiskExplorerHtmlExportRequest { Rows = document.Rows, Title = "Home Archive" }
            });
            Assert.True(writeResult.Success, $"Export failed: {writeResult.Error}");
        }

        Assert.Single(Directory.GetDirectories(workDirectory));
        var reread = await ReadAsync(targetPath);
        Assert.Equal(document.Rows.Count, reread.Rows.Count);
        var original = IndexById(document);
        foreach (var (id, row) in IndexById(reread))
        {
            Assert.True(original.TryGetValue(id, out var expected), $"Unexpected row {id} after round trip");
            Assert.Equal(TextOf(expected, "name"), TextOf(row, "name"));
            Assert.Equal(TextOf(expected, "parentId"), TextOf(row, "parentId"));
            Assert.Equal(TextOf(expected, "type"), TextOf(row, "type"));
            Assert.Equal(expected["size"], row["size"]);
        }
    }

    [Fact]
    public async Task Export_RequiresFileTarget()
    {
        var service = CreateService();
        await using var target = new MemoryStream();
        var exception = await Assert.ThrowsAsync<FileFormatWriteFailedException>(() => service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "disk-explorer-html-report",
            Target = target,
            FileName = "report.html",
            Payload = new DiskExplorerHtmlExportRequest
            {
                Rows = new List<Dictionary<string, object?>>()
            }
        }));

        Assert.Contains("file target", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelledImport_ThrowsOperationCanceled()
    {
        var service = CreateService();
        var path = FixturePath("complete", "index.html");
        await using var source = File.OpenRead(path);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(
            new FileFormatReadRequest
            {
                FormatId = "disk-explorer-html-report",
                Source = source,
                FileName = "index.html",
                SourcePath = path
            },
            cancelled.Token));
    }

    [Fact]
    public async Task DeepChain_StopsAtDepthLimit()
    {
        var root = Path.Combine(workDirectory, "deep");
        var bundle = Path.Combine(root, "deep");
        Directory.CreateDirectory(bundle);
        var indexLinks = new StringBuilder();
        indexLinks.AppendLine("<!DOCTYPE HTML><html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\"><title>D</title></head><body>");
        indexLinks.AppendLine("<div class=\"dx-report\" data-title=\"D\"><div class=\"dx-item\" data-itemtype=\"Volume\" data-name=\"V\"><a class=\"dx-name\" href=\"deep/p0000.html\">V</a></div></div></body></html>");
        File.WriteAllText(Path.Combine(root, "index.html"), indexLinks.ToString());

        for (var i = 0; i < 80; i++)
        {
            var next = i + 1 < 80 ? $"p{i + 1:0000}.html" : "leaf.html";
            var leaf = i + 1 >= 80;
            var page = $"<!DOCTYPE HTML><html><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\"><title>L{i}</title></head><body>" +
                $"<div class=\"dx-report\" data-title=\"D\"><div class=\"dx-item\" data-itemtype=\"Folder\" data-name=\"L{i}\"><span class=\"dx-name\">L{i}</span>" +
                (leaf ? "<div class=\"dx-item\" data-itemtype=\"File\" data-name=\"deep.txt\" data-size=\"1\"><span class=\"dx-name\">deep.txt</span></div>"
                    : $"<a class=\"dx-child\" href=\"{next}\">L{i + 1}</a>") + "</div></div></body></html>";
            File.WriteAllText(Path.Combine(bundle, i == 0 ? "p0000.html" : $"p{i:0000}.html"), page);
        }

        var service = CreateService();
        var indexPath = Path.Combine(root, "index.html");
        await using var source = File.OpenRead(indexPath);
        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "disk-explorer-html-report",
            Source = source,
            FileName = "index.html",
            SourcePath = indexPath
        });

        Assert.True(result.Success, $"Import failed: {result.Error}");
        var document = Assert.IsType<DiskExplorerHtmlReportDocument>(result.Payload);
        Assert.True(document.Summary.Partial);
        Assert.Contains(document.Summary.Notes, note => note.Contains("depth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PathSafety_RejectsEscapesAndAcceptsContainedPaths()
    {
        var root = Path.Combine(workDirectory, "root");
        Directory.CreateDirectory(Path.Combine(root, "sub"));

        Assert.True(DiskExplorerHtmlPath.TryResolveWithinRoot(root, root, "sub/page.html", out var inside, out _));
        Assert.EndsWith(Path.Combine("sub", "page.html"), inside, StringComparison.OrdinalIgnoreCase);
        Assert.True(DiskExplorerHtmlPath.TryResolveWithinRoot(root, Path.Combine(root, "sub"), "../sub/page.html", out _, out _));
        Assert.True(DiskExplorerHtmlPath.TryResolveWithinRoot(root, Path.Combine(root, "sub"), "../index.html", out _, out _));

        Assert.False(DiskExplorerHtmlPath.TryResolveWithinRoot(root, root, "../../escape.html", out _, out var escape));
        Assert.Contains("bundle", escape, StringComparison.OrdinalIgnoreCase);
        Assert.False(DiskExplorerHtmlPath.TryResolveWithinRoot(root, root, "/etc/passwd", out _, out _));
        Assert.False(DiskExplorerHtmlPath.TryResolveWithinRoot(root, root, @"C:\Windows\win.ini", out _, out _));
        Assert.False(DiskExplorerHtmlPath.TryResolveWithinRoot(root, root, @"\\server\share\x.html", out _, out _));
        Assert.False(DiskExplorerHtmlPath.TryResolveWithinRoot(root, root, "https://example.invalid/x.html", out _, out _));
        Assert.False(DiskExplorerHtmlPath.TryResolveWithinRoot(root, root, "javascript:alert(1)", out _, out _));
        Assert.False(DiskExplorerHtmlPath.TryResolveWithinRoot(root, root, "#frag", out _, out _));
        Assert.True(DiskExplorerHtmlPath.IsRemoteOrSpecialScheme("https://example.invalid/x.html"));
        Assert.False(DiskExplorerHtmlPath.IsRemoteOrSpecialScheme("sub/page.html"));
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
