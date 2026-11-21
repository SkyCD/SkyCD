using System;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.GCstar;
using SkyCD.Plugin.GCstar.Exceptions;
using Xunit;
using SkyCD.Plugin.GCstar.Models;
using SkyCD.Plugin.GCstar.Parsing;

namespace SkyCD.Plugin.GCstar.Tests;

public sealed class GCstarParserTests
{
    private static GCstarParseResult ParseFixture(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GCstar", fileName);
        using var stream = File.OpenRead(fixturePath);
        return GCstarParser.Parse(stream, CancellationToken.None);
    }

    private static GCstarParseResult ParseText(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return GCstarParser.Parse(stream, CancellationToken.None);
    }

    [Fact]
    public void Parse_MoviesFixture_ReadsCollectionMetadata()
    {
        var result = ParseFixture("movies.gcs");

        Assert.Equal("GCmovies", result.CollectionType);
        Assert.Equal("1.6.1", result.Version);
        Assert.Equal("My Movies", result.CollectionName);
        Assert.Equal("My Movies", result.Information["name"]);
        Assert.Equal("tester", result.Information["owner"]);
    }

    [Fact]
    public void Parse_MoviesFixture_KeepsTwoItemsAndSkipsTwoWithReasons()
    {
        var result = ParseFixture("movies.gcs");

        Assert.Equal(2, result.Items.Count);
        Assert.NotNull(result.Items[0]);
        Assert.NotNull(result.Items[1]);
        Assert.Equal(0, result.Items[0]!.Index);
        Assert.Equal(1, result.Items[1]!.Index);
        Assert.Equal(2, result.SkippedItemsCount);
        Assert.Equal(2, result.SkippedReasons.Count);
        Assert.Contains(result.SkippedReasons, reason => reason.Contains("unregistered-version placeholder"));
        Assert.Contains(result.SkippedReasons, reason => reason.Contains("without an id"));
    }

    [Fact]
    public void Parse_MoviesFixture_ReadsRawFieldValuesVerbatim()
    {
        var result = ParseFixture("movies.gcs");
        var fields = result.Items[0]!.Fields;

        Assert.Equal("1", fields["id"]);
        Assert.Equal("The Matrix", fields["name"]);
        Assert.Equal("The Matrix", fields["__name"]);
        Assert.Equal("Alice", fields["borrower"]);
        Assert.Equal("2024-03-01", fields["lendDate"]);
        Assert.Equal(
            "Alice | 2024-03-01 |  | Bob | 2023-01-05 | 2023-01-20 |",
            fields["borrowings"]);
        Assert.Equal("sci-fi, action", fields["tags"]);
        Assert.Equal("0978399999991", fields["ean"]);
        Assert.Equal("covers/matrix.jpg", fields["cover"]);
        Assert.Equal("A hacker learns <the> truth & \"reality\".", fields["description"]);
        Assert.Equal("Watch again", fields["comments"]);
    }

    [Fact]
    public void Parse_MoviesFixture_FallsBackToTitleForMissingName()
    {
        var result = ParseFixture("movies.gcs");

        Assert.Equal("Half-Life 2", result.Items[1]!.Fields["__name"]);
    }

    [Fact]
    public void Parse_MoviesFixture_RetainsUnknownDataAsExtensionMetadata()
    {
        var result = ParseFixture("movies.gcs");
        var item = result.Items[0]!;

        Assert.Equal("A-42", item.Fields["shelfcode"]);
        Assert.Equal("A-42", item.ExtraFields["GCstar.shelfcode"]);
        Assert.Equal("kept as extension metadata", item.Fields["customnote"]);
        Assert.Equal("kept as extension metadata", item.ExtraFields["GCstar.customnote"]);
        Assert.Contains("GCstar.shelfcode", result.UnsupportedFields);
        Assert.Contains("GCstar.customnote", result.UnsupportedFields);
        Assert.DoesNotContain(result.UnsupportedFields, field => field.StartsWith("item@id", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_BooksFixture_ReadsInformationAttributesAndSecrets()
    {
        var result = ParseFixture("gcstar-books-minimal.gcs");

        Assert.Equal("GCbooks", result.CollectionType);
        Assert.Equal("Bedside Books", result.CollectionName);
        Assert.Single(result.Items);
        Assert.Equal("The Hobbit", result.Items[0]!.Fields["__name"]);
        Assert.Equal("Bilbo leaves the Shire.", result.Items[0]!.Fields["description"]);
        Assert.Equal("First edition dust jacket notes.", result.Items[0]!.Fields["secrets"]);
        Assert.Equal(0, result.SkippedItemsCount);
    }

    [Fact]
    public void Parse_EmptyCollection_ThrowsEmptyReport()
    {
        Assert.Throws<GCstarEmptyReportException>(() => ParseFixture("empty.gcs"));
    }

    [Fact]
    public void Parse_WrongRoot_ThrowsUnsupportedSchema()
    {
        Assert.Throws<GCstarUnsupportedSchemaException>(() => ParseFixture("wrong-root.xml"));
    }

    [Fact]
    public void Parse_NestedItem_ThrowsMalformedDocument()
    {
        Assert.Throws<GCstarMalformedDocumentException>(() => ParseFixture("nested-item.gcs"));
    }

    [Fact]
    public void Parse_DtdEntity_ThrowsMalformedDocument()
    {
        Assert.Throws<GCstarMalformedDocumentException>(() => ParseFixture("dtd-entity.gcs"));
    }

    [Fact]
    public void Parse_TruncatedXml_ThrowsMalformedDocument()
    {
        Assert.Throws<GCstarMalformedDocumentException>(
            () => ParseText("<collection type=\"GCmovies\"><item id=\"1\""));
    }

    [Fact]
    public void Parse_EmptyStream_ThrowsMalformedDocument()
    {
        Assert.Throws<GCstarMalformedDocumentException>(
            () => ParseText(string.Empty));
    }

    [Fact]
    public void Parse_EmptyElementItem_IsKept()
    {
        var result = ParseText(
            "<collection type=\"GCmovies\" version=\"1\">" +
            "<item id=\"9\" name=\"Short\" /></collection>");

        Assert.Single(result.Items);
        Assert.Equal("Short", result.Items[0]!.Fields["__name"]);
    }

    [Fact]
    public void Parse_DuplicateAttribute_KeepsFirstCaseInsensitive()
    {
        var result = ParseText(
            "<collection type=\"GCmovies\" version=\"1\">" +
            "<item id=\"1\" ID=\"2\" name=\"X\" /></collection>");

        Assert.Equal("1", result.Items[0]!.Fields["id"]);
    }

    [Fact]
    public void Parse_CancelledToken_ThrowsOperationCanceled()
    {
        using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes("<collection type=\"GCmovies\"><item id=\"1\" name=\"X\" /></collection>"));
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => GCstarParser.Parse(stream, source.Token));
    }

    [Fact]
    public void Parse_DeeplyNestedContent_ThrowsMalformedDocument()
    {
        var builder = new StringBuilder("<collection type=\"GCmovies\" version=\"1\">");
        for (var i = 0; i < GCstarParser.MaxDepth + 10; i++)
        {
            builder.Append("<level").Append(i).Append('>');
        }

        builder.Append("x");
        Assert.Throws<GCstarMalformedDocumentException>(() => ParseText(builder.ToString()));
    }
}
