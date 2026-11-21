using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.GCstar;
using Xunit;
using SkyCD.Plugin.GCstar.Models;
using SkyCD.Plugin.GCstar.Parsing;

namespace SkyCD.Plugin.GCstar.Tests;

public sealed class GCstarDetectionTests
{
    private readonly GCstarPlugin plugin = new();

    private static MemoryStream ToStream(string xml) =>
        new(Encoding.UTF8.GetBytes(xml));

    [Fact]
    public void SupportedFormat_HasExpectedMetadata()
    {
        var format = plugin.SupportedFormat;

        Assert.Equal("gcstar", format.FormatId);
        Assert.Equal("GCstar Collection (*.gcs)", format.DisplayName);
        Assert.Contains(".gcs", format.Extensions);
        Assert.Contains(".xml", format.Extensions);
        Assert.True(format.CanRead);
        Assert.False(format.CanWrite);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForMoviesFixture()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GCstar", "movies.gcs");
        using var stream = File.OpenRead(fixturePath);

        Assert.True(plugin.CanHandleContent(stream, "movies.gcs"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForMinimalInlineCollection()
    {
        using var stream = ToStream(
            "<collection type=\"GCbooks\" version=\"1.6.1\" items=\"1\">" +
            "<information><name>Books</name></information>" +
            "<item id=\"b1\" title=\"Dune\" borrower=\"Bob\" />" +
            "</collection>");

        Assert.True(GCstarRecognizer.IsGCstarCollection(stream));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsTrueForEmptyButWellFormedCollection()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GCstar", "empty.gcs");
        using var stream = File.OpenRead(fixturePath);

        Assert.True(plugin.CanHandleContent(stream, "empty.gcs"));
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForSkyCdNativeXml()
    {
        using var stream = ToStream(
            "<skycd:catalog xmlns:skycd=\"urn:skycd:catalog\" schemaVersion=\"1.0\">\r\n" +
            "  <skycd:node nodeId=\"1\" parentId=\"\" kind=\"folder\" name=\"Root\" sizeBytes=\"0\"/>\r\n" +
            "</skycd:catalog>");

        Assert.False(plugin.CanHandleContent(stream, "catalog.xml"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForUnrelatedCollectionXml()
    {
        using var stream = ToStream(
            "<collection><item><name>Generic</name></item></collection>");

        Assert.False(plugin.CanHandleContent(stream, "generic.xml"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ReturnsFalseForNonXml()
    {
        using var stream = ToStream("not xml at all < < <");

        Assert.False(plugin.CanHandleContent(stream, "broken.gcs"));
    }

    [Fact]
    public async Task ReadAsync_MoviesFixture_ReturnsDocumentWithRows()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GCstar", "movies.gcs");
        await using var stream = File.OpenRead(fixturePath);
        var progress = new RecordingProgress();

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "gcstar",
            FileName = "movies.gcs",
            Progress = progress
        });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<GCstarReportDocument>(result.Payload);
        Assert.Equal(3, document.Count);
        Assert.Contains(100, progress.Values);
    }

    [Fact]
    public async Task ReadAsync_WrongRoot_ReturnsFailure()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GCstar", "wrong-root.xml");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "gcstar",
            FileName = "wrong-root.xml"
        });

        Assert.False(result.Success);
        Assert.Contains("GCstar", result.Error);
    }

    [Fact]
    public async Task ReadAsync_NestedItem_ReturnsFailure()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GCstar", "nested-item.gcs");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "gcstar",
            FileName = "nested-item.gcs"
        });

        Assert.False(result.Success);
        Assert.Contains("malformed", result.Error);
    }

    [Fact]
    public async Task ReadAsync_EmptyCollection_ReturnsFailure()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GCstar", "empty.gcs");
        await using var stream = File.OpenRead(fixturePath);

        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            Source = stream,
            FormatId = "gcstar",
            FileName = "empty.gcs"
        });

        Assert.False(result.Success);
        Assert.Contains("no catalog entries", result.Error);
    }

    [Fact]
    public async Task WriteAsync_IsNotSupported()
    {
        await using var target = new MemoryStream();
        var result = await plugin.WriteAsync(new FileFormatWriteRequest
        {
            Target = target,
            FormatId = "gcstar",
            Payload = new object()
        });

        Assert.False(result.Success);
    }

    private sealed class RecordingProgress : IProgress<int>
    {
        public List<int> Values { get; } = [];

        public void Report(int value) => Values.Add(value);
    }
}
