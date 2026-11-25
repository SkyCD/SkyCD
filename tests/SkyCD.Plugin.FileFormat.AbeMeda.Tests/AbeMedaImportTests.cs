using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.AbeMeda;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.FileFormat.AbeMeda.Import;
using SkyCD.Plugin.FileFormat.AbeMeda.Models;
using SkyCD.Plugin.FileFormat.AbeMeda.Parsing;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Tests;

public class AbeMedaImportTests
{
    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new AbeMedaPlugin()]);
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "AbeMeda", name);

    private static async Task<AbeMedaReportDocument> ReadAsync(string name)
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath(name));
        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "abemeda-xml",
            Source = source,
            FileName = name
        });

        Assert.True(result.Success, $"Import failed: {result.Error}");
        return Assert.IsType<AbeMedaReportDocument>(result.Payload);
    }

    private static Dictionary<string, Dictionary<string, object?>> IndexById(AbeMedaReportDocument document)
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
    public void GetSaveFormats_RegistersAbeMedaAsReadOnly()
    {
        var service = CreateService();

        Assert.Contains(service.GetOpenFormats(),
            format => format.FormatId == "abemeda-xml" && format.Extensions.Contains(".xml"));
        Assert.DoesNotContain(service.GetSaveFormats(),
            format => format.FormatId == "abemeda-xml");
    }

    [Fact]
    public async Task NestedExport_PreservesHierarchyAcrossVolumes()
    {
        var document = await ReadAsync("nested-two-volumes.xml");
        var byId = IndexById(document);

        Assert.Equal(2 + 3 + 7, document.Rows.Count);

        var backup = byId["volume:Backup 2024"];
        Assert.Equal("media", TextOf(backup, "type"));
        Assert.Null(backup["parentId"]);

        var documents = byId["folder:Backup 2024/Documents"];
        Assert.Equal("volume:Backup 2024", TextOf(documents, "parentId"));
        Assert.Equal("folder:Backup 2024/Documents",
            TextOf(byId["folder:Backup 2024/Documents/Projects"], "parentId"));

        var photos = byId["volume:Photos"];
        Assert.Equal("volume:Photos", TextOf(byId["file:Photos/beach.png"], "parentId"));

        var loose = byId["file:Backup 2024/loose.iso"];
        Assert.Equal("volume:Backup 2024", TextOf(loose, "parentId"));
    }

    [Fact]
    public async Task NestedExport_RetainsCommentsDatesSizesMetadataAndUnknowns()
    {
        var document = await ReadAsync("nested-two-volumes.xml");
        var byId = IndexById(document);

        var readme = byId["file:Backup 2024/Documents/readme.txt"];
        Assert.Equal(512L, readme["size"]);
        Assert.Equal("read me", TextOf(PropertiesOf(readme), "Comment"));
        Assert.Equal("Admin", Convert.ToString(PropertiesOf(readme)["AbeMeda.File.Author"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("5", Convert.ToString(PropertiesOf(readme)["AbeMeda.File.CustomRating"], System.Globalization.CultureInfo.InvariantCulture));

        var unicode = byId["file:Backup 2024/Musik/Grüße.mp3"];
        Assert.Equal("Grüße.mp3", TextOf(unicode, "name"));
        Assert.Equal("Künstler", Convert.ToString(PropertiesOf(unicode)["AbeMeda.File.Artist"], System.Globalization.CultureInfo.InvariantCulture));

        var sunset = byId["file:Backup 2024/Documents/sunset.jpg"];
        Assert.Equal("true", Convert.ToString(PropertiesOf(sunset)["HasThumbnail"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.False(PropertiesOf(sunset).ContainsKey("Thumbnail"));

        var duplicate = byId["file:Backup 2024/Documents/Projects/readme.txt"];
        Assert.Equal("project notes", TextOf(PropertiesOf(duplicate), "Comment"));

        var documents = PropertiesOf(byId["folder:Backup 2024/Documents"]);
        Assert.Equal("2024-01-05T10:00:00.0000000+00:00", Convert.ToString(documents["Created"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("docs folder", TextOf(documents, "Comment"));
        Assert.Equal("D:\\Documents", Convert.ToString(documents["OriginalPath"], System.Globalization.CultureInfo.InvariantCulture));

        var volumeProperties = PropertiesOf(byId["volume:Backup 2024"]);
        Assert.Equal("6.2", Convert.ToString(volumeProperties["AbeMedaSchemaVersion"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("AbeMeda", Convert.ToString(volumeProperties["AbeMedaSource"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("Red", Convert.ToString(volumeProperties["Label"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task NestedExport_SummaryCountsAndWarnings()
    {
        var document = await ReadAsync("nested-two-volumes.xml");

        Assert.Equal(2, document.Summary.Volumes);
        Assert.Equal(3, document.Summary.Folders);
        Assert.Equal(7, document.Summary.Files);
        Assert.Equal("Nested", document.Summary.ImportMode);
        Assert.Equal("6.2", document.Summary.SchemaVersion);
        Assert.Contains(document.Summary.Notes, note => note.Contains("CustomRating", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FlatExport_DerivesParentsFromPaths()
    {
        var document = await ReadAsync("flat-find-results.xml");
        var byId = IndexById(document);

        Assert.Equal("Flat", document.Summary.ImportMode);
        Assert.Equal(1, document.Summary.Volumes);

        var readme = byId["file:Backup 2024/Documents/readme.txt"];
        Assert.Equal("folder:Backup 2024/Documents", TextOf(readme, "parentId"));
        Assert.Equal("folder:Backup 2024/Documents/Projects", TextOf(byId["file:Backup 2024/Documents/Projects/app.exe"], "parentId"));

        var projects = byId["folder:Backup 2024/Documents/Projects"];
        Assert.Equal("folder:Backup 2024/Documents", TextOf(projects, "parentId"));

        var loose = byId["file:Backup 2024/loose.iso"];
        Assert.Equal("Image", Convert.ToString(PropertiesOf(loose)["ExportKind"], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task MalformedXml_ThrowsReadFailedWithDiagnostic()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("malformed.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "abemeda-xml",
            Source = source,
            FileName = "malformed.xml"
        }));

        Assert.Contains("malformed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyExport_ThrowsReadFailedWithDiagnostic()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("empty.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "abemeda-xml",
            Source = source,
            FileName = "empty.xml"
        }));

        Assert.Contains("no catalog entries", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongRoot_ThrowsReadFailedWithDiagnostic()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("wrong-root.xml"));
        var exception = await Assert.ThrowsAsync<FileFormatReadFailedException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "abemeda-xml",
            Source = source,
            FileName = "wrong-root.xml"
        }));

        Assert.Contains("not a supported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detection_AcceptsFamilyRootsAndRejectsOthers()
    {
        var plugin = new AbeMedaPlugin();

        foreach (var name in new[] { "nested-two-volumes.xml", "flat-find-results.xml", "empty.xml", "malformed.xml" })
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

        using var wincatalog = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><WinCatalog version=\"2024\"><Disk Name=\"D\" /></WinCatalog>"));
        Assert.False(plugin.CanHandleContent(wincatalog, "other.xml"));

        using var content = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><AbeMeda />"));
        using var nonSeekable = new NonSeekableStream(content);
        Assert.False(plugin.CanHandleContent(nonSeekable, "x.xml"));
        Assert.Equal(0, content.Position);
        Assert.True(content.CanRead);
    }

    [Fact]
    public async Task NonSeekableSource_ImportsAfterContentDetection()
    {
        var plugin = new AbeMedaPlugin();
        await using var content = File.OpenRead(FixturePath("nested-two-volumes.xml"));
        await using var source = new NonSeekableStream(content);

        Assert.False(plugin.CanHandleContent(source, "export.xml"));
        var result = await CreateService().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "abemeda-xml",
            Source = source,
            FileName = "export.xml"
        });

        Assert.True(result.Success);
        Assert.Equal(12, Assert.IsType<AbeMedaReportDocument>(result.Payload).Rows.Count);
        Assert.True(content.CanRead);
    }

    [Fact]
    public async Task CancelledImport_ThrowsOperationCanceled()
    {
        var service = CreateService();
        await using var source = File.OpenRead(FixturePath("nested-two-volumes.xml"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync(
            new FileFormatReadRequest
            {
                FormatId = "abemeda-xml",
                Source = source,
                FileName = "nested-two-volumes.xml"
            },
            cancelled.Token));

        Assert.Equal(cancelled.Token, exception.CancellationToken);
    }

    [Fact]
    public void MissingAttributes_DoNotAbortImport()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <AbeMeda version="6.2">
              <Volume Name="V">
                <Folder Name="F">
                  <File Name="bare.txt" />
                  <File />
                </Folder>
              </Volume>
            </AbeMeda>
            """;
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var parseResult = AbeMedaParser.Parse(stream, CancellationToken.None);
        var document = AbeMedaHierarchyBuilder.Compose(parseResult);

        Assert.Equal(3, document.Rows.Count);
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
