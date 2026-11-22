using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using Xunit;
using SkyCD.Plugin.FileFormat.Tellico.Models;

namespace SkyCD.Plugin.FileFormat.Tellico.Tests;

public sealed class TellicoDetectionTests
{
    private readonly TellicoPlugin plugin = new();

    private async Task<FileFormatReadResult> ReadBytesAsync(byte[] bytes, string fileName)
    {
        using var stream = new MemoryStream(bytes);
        return await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "tellico",
            FileName = fileName
        });
    }

    [Fact]
    public void SupportedFormat_HasExpectedMetadata()
    {
        var format = plugin.SupportedFormat;

        Assert.Equal("tellico", format.FormatId);
        Assert.Equal("Tellico Collection (*.tc)", format.DisplayName);
        Assert.Contains(".tc", format.Extensions);
        Assert.Contains(".xml", format.Extensions);
        Assert.True(format.CanRead);
        Assert.False(format.CanWrite);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForArchive()
    {
        using var stream = new MemoryStream(TellicoFixtureBuilder.BooksArchive());

        Assert.True(plugin.CanHandleContent(stream, "books.tc"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForXmlExport()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(TellicoFixtureBuilder.BooksXml()));

        Assert.True(plugin.CanHandleContent(stream, "books.xml"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForOtherZip()
    {
        using var output = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("readme.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("hello");
        }

        using var stream = new MemoryStream(output.ToArray());
        Assert.False(plugin.CanHandleContent(stream, "other.zip"));
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForOtherXml()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "<collection><item id=\"1\" name=\"x\" /></collection>"));

        Assert.False(plugin.CanHandleContent(stream, "other.xml"));
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForNonXml()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("definitely not a collection"));

        Assert.False(plugin.CanHandleContent(stream, "notes.tc"));
    }

    [Fact]
    public async Task ReadAsync_Archive_ReturnsRowsWithEntryKind()
    {
        var result = await ReadBytesAsync(TellicoFixtureBuilder.BooksArchive(), "books.tc");

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<TellicoReportDocument>(result.Payload);
        Assert.Equal(3, document.Count);

        var root = document.Rows[0];
        Assert.Equal("media", root["kind"]);
        Assert.Equal("My Books", root["name"]);

        var entries = document.Rows.Skip(1).ToList();
        Assert.All(entries, row => Assert.Equal("entry", row["kind"]));
        Assert.Contains(entries, row => string.Equals((string)row["name"]!, "Dune", StringComparison.Ordinal));
        Assert.Contains(entries, row => string.Equals((string)row["name"]!, "Neuromancer", StringComparison.Ordinal));

        var dune = entries.Single(row => string.Equals((string)row["name"]!, "Dune", StringComparison.Ordinal));
        var properties = Assert.IsType<Dictionary<string, object?>>(dune["properties"]);
        Assert.Equal(1965L, properties["Pub_year"]);
        Assert.Equal("Alice", properties["Borrower"]);
        Assert.Equal("cover1", properties["Cover"]);
        Assert.Equal("JPEG", properties["CoverFormat"]);
        Assert.Equal("kept verbatim", Assert.IsType<Dictionary<string, object?>>(
            entries.Single(row => string.Equals((string)row["name"]!, "Neuromancer", StringComparison.Ordinal))["properties"])["Tellico.unknown_field"]);

        Assert.Equal(1, document.Summary.Collections);
        Assert.Equal(2, document.Summary.Entries);
        Assert.Equal("11", document.Summary.SyntaxVersion);
    }

    [Fact]
    public async Task ReadAsync_MusicXml_BuildsEntries()
    {
        var result = await ReadBytesAsync(Encoding.UTF8.GetBytes(TellicoFixtureBuilder.MusicXml()), "music.xml");

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<TellicoReportDocument>(result.Payload);
        Assert.Equal(3, document.Count);
        Assert.Contains(document.Rows, row => string.Equals((string)row["name"]!, "Blue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadAsync_WrongRoot_ReturnsFailure()
    {
        var result = await ReadBytesAsync(Encoding.UTF8.GetBytes("<catalog/>"), "wrong.xml");

        Assert.False(result.Success);
        Assert.Contains("Tellico", result.Error);
    }

    [Fact]
    public async Task WriteAsync_IsNotSupported()
    {
        await using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = "tellico",
            Payload = new object()
        });

        Assert.False(result.Success);
    }
}
