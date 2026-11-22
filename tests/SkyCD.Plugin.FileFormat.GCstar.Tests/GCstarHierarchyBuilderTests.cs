using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SkyCD.Plugin.FileFormat.GCstar;
using Xunit;
using SkyCD.Plugin.FileFormat.GCstar.Import;
using SkyCD.Plugin.FileFormat.GCstar.Models;
using SkyCD.Plugin.FileFormat.GCstar.Parsing;

namespace SkyCD.Plugin.FileFormat.GCstar.Tests;

public sealed class GCstarHierarchyBuilderTests
{
    private static GCstarReportDocument ComposeFixture(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "GCstar", fileName);
        using var stream = File.OpenRead(fixturePath);
        var parseResult = GCstarParser.Parse(stream, CancellationToken.None);
        return GCstarHierarchyBuilder.Compose(parseResult);
    }

    private static Dictionary<string, object?> RowByName(GCstarReportDocument document, string name) =>
        document.Rows.Single(row => string.Equals((string)row["name"]!, name, StringComparison.Ordinal));

    private static Dictionary<string, object?> PropertiesOf(Dictionary<string, object?> row) =>
        Assert.IsType<Dictionary<string, object?>>(row["properties"]);

    [Fact]
    public void Compose_MoviesFixture_BuildsRootMediaNode()
    {
        var document = ComposeFixture("movies.gcs");

        Assert.Equal(3, document.Count);
        var root = document.Rows[0];
        Assert.Equal("gcstar:collection", root["nodeId"]);
        Assert.Null(root["parentId"]);
        Assert.Equal("media", root["kind"]);
        Assert.Equal("My Movies", root["name"]);
        Assert.Equal(734003200L, root["sizeBytes"]);
        Assert.Equal(2L, root["childrenCount"]);

        var properties = PropertiesOf(root);
        Assert.Equal("GCmovies", properties["CollectionType"]);
        Assert.Equal("1.6.1", properties["FormatVersion"]);
        Assert.Equal("tester", properties["owner"]);
        Assert.Equal("2", properties["ItemCount"]);
        Assert.Equal("2", properties["SkippedItems"]);
        Assert.Contains("GCstar.shelfcode", (string)properties["UnsupportedFields"]!);
    }

    [Fact]
    public void Compose_MoviesFixture_TypesFirstItemProperties()
    {
        var document = ComposeFixture("movies.gcs");
        var properties = PropertiesOf(RowByName(document, "The Matrix"));

        Assert.Equal("1", properties["GCstarId"]);
        Assert.Equal("The Matrix", properties["Title"]);
        Assert.Equal("Alice", properties["Borrower"]);
        Assert.Equal("2024-03-01", properties["LendDate"]);
        Assert.Equal("2024-03-01", properties["LendDateNormalized"]);
        Assert.Equal(
            "Alice | 2024-03-01 |  | Bob | 2023-01-05 | 2023-01-20 |",
            properties["Borrowings"]);
        Assert.Equal("true", properties["Favourite"]);
        Assert.Equal("true", properties["Case"]);
        Assert.Equal("true", properties["Manual"]);
        Assert.Equal("A hacker learns <the> truth & \"reality\".", properties["Description"]);
        Assert.Equal("Watch again", properties["Comments"]);
        Assert.Equal("covers/matrix.jpg", properties["Cover"]);
        Assert.Equal("covers/matrix.jpg", properties["Image"]);
        Assert.Equal("0978399999991", properties["Ean"]);
        Assert.Equal("DVD", properties["Platform"]);
        Assert.Equal("1999-03-31", properties["Released"]);
        Assert.Equal("1999-03-31", properties["ReleasedNormalized"]);
        Assert.Equal("A-42", properties["GCstar.shelfcode"]);

        Assert.Equal(9L, properties["Rating"]);
        var tags = Assert.IsType<List<string>>(properties["Tags"]);
        Assert.Equal(["sci-fi", "action"], tags);
    }

    [Fact]
    public void Compose_MoviesFixture_SplitsMultiValueFieldsAndSizesSecondItem()
    {
        var document = ComposeFixture("movies.gcs");
        var row = RowByName(document, "Half-Life 2");

        Assert.Equal("gcstar:item:2", row["nodeId"]);
        Assert.Equal("gcstar:collection", row["parentId"]);
        Assert.Equal("file", row["kind"]);
        Assert.Equal(734003200L, row["sizeBytes"]);

        var properties = PropertiesOf(row);
        Assert.Equal(["Action", "Sci-Fi"], Assert.IsType<List<string>>(properties["Genre"]));
        Assert.Equal(["1", "2"], Assert.IsType<List<string>>(properties["Players"]));
        Assert.Equal(["Shelf 3", "Box 7"], Assert.IsType<List<string>>(properties["Location"]));
        Assert.Equal(700L, properties["InstallationSize"]);
        Assert.Equal("MB", properties["InstallationSizeUnit"]);
        Assert.Equal(9.5, properties["Rating"]);
        Assert.Equal("false", properties["Case"]);
        Assert.Equal("false", properties["Exclusive"]);
        Assert.Equal("2024-01-15", properties["DateAdded"]);
        Assert.Equal("2024-01-15", properties["DateAddedNormalized"]);
    }

    [Fact]
    public void Compose_MoviesFixture_FillsSummary()
    {
        var document = ComposeFixture("movies.gcs");

        Assert.Equal(1, document.Summary.Volumes);
        Assert.Equal(2, document.Summary.Files);
        Assert.Equal(2, document.Summary.SkippedRows);
        Assert.Equal("GCmovies", document.Summary.CollectionType);
        Assert.Equal("1.6.1", document.Summary.Version);
        Assert.Equal("My Movies", document.Summary.CollectionName);
        Assert.Contains(document.Summary.Notes, note => note.Contains("GCstar.shelfcode"));
        Assert.Equal(3, document.Summary.Notes.Count);
    }

    [Fact]
    public void Compose_BooksFixture_UsesInformationNameForRoot()
    {
        var document = ComposeFixture("gcstar-books-minimal.gcs");

        Assert.Equal(2, document.Count);
        Assert.Equal("Bedside Books", document.Rows[0]["name"]);
        var item = PropertiesOf(document.Rows[1]);
        Assert.Equal("The Hobbit", document.Rows[1]["name"]);
        Assert.Equal("Bilbo leaves the Shire.", item["Description"]);
        Assert.Equal("First edition dust jacket notes.", item["Secrets"]);
    }

    [Fact]
    public void Compose_DuplicateIds_ProducesUniqueNodeIds()
    {
        var parseResult = new GCstarParseResult(
            "GCmovies",
            "1",
            "Dupes",
            new Dictionary<string, string>(),
            new List<GCstarItem?>
            {
                new(0, new Dictionary<string, string> { ["id"] = "7", ["__name"] = "First" }, new Dictionary<string, string>()),
                new(1, new Dictionary<string, string> { ["id"] = "7", ["__name"] = "Second" }, new Dictionary<string, string>())
            },
            Array.Empty<string>(),
            0,
            Array.Empty<string>());

        var document = GCstarHierarchyBuilder.Compose(parseResult);

        var nodeIds = document.Rows.Skip(1).Select(row => (string)row["nodeId"]!).ToList();
        Assert.Equal(2, nodeIds.Distinct(StringComparer.Ordinal).Count());
    }
}
