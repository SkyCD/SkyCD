using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Tellico;
using SkyCD.Plugin.Tellico.Exceptions;
using Xunit;

namespace SkyCD.Plugin.Tellico.Tests;

public sealed class TellicoParserTests
{
    private static TellicoParseResult ParseText(string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return TellicoParser.Parse(stream, CancellationToken.None);
    }

    private static TellicoParseResult ParseBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return TellicoParser.Parse(stream, CancellationToken.None);
    }

    [Fact]
    public void Parse_BooksXml_MapsTypedFields()
    {
        var result = ParseText(TellicoFixtureBuilder.BooksXml());

        Assert.Equal("11", result.SyntaxVersion);
        var collection = Assert.Single(result.Collections);
        Assert.Equal("My Books", collection.Title);
        Assert.Equal("2", collection.Type);
        Assert.Equal("Books", collection.EntryTitle);
        Assert.Equal(11, collection.FieldDefinitions.Count);

        var first = collection.Entries[0]!;
        Assert.Equal("1", first.Id);
        Assert.Equal("Dune", first.Fields["title"]);
        Assert.Equal("Frank Herbert", first.Fields["author"]);
        Assert.Equal(1965L, first.Fields["pub_year"]);
        Assert.Equal("true", first.Fields["read"]);
        Assert.Equal(5L, first.Fields["rating"]);
        Assert.Equal("cover1", first.Fields["cover"]);
        Assert.Equal("JPEG", first.Fields["coverFormat"]);
        Assert.Equal("100x150", first.Fields["coverDimensions"]);
        Assert.Equal("2020-05-17", first.Fields["purchase_date"]);
        Assert.Equal("2020-05-17", first.Fields["purchase_dateNormalized"]);
        Assert.Equal("First edition", first.Fields["custom_note"]);
        Assert.Equal("Dune", first.Fields["bibtex"]);

        var second = collection.Entries[1]!;
        Assert.Equal("Neuromancer", second.Fields["title"]);
        Assert.Equal("false", second.Fields["read"]);
        Assert.Equal("kept verbatim", second.Fields["Tellico.unknown_field"]);
        Assert.Contains("Tellico.unknown_field", collection.UnsupportedFields);
    }

    [Fact]
    public void Parse_BooksXml_MapsLoansToEntries()
    {
        var result = ParseText(TellicoFixtureBuilder.BooksXml());
        var first = result.Collections[0].Entries[0]!;

        Assert.Equal("Alice", first.Fields["Borrower"]);
        Assert.Equal("2024-01-05", first.Fields["LoanDate"]);
        Assert.Equal("2024-02-05", first.Fields["DueDate"]);
        Assert.Equal("u1", first.Fields["LoanUid"]);

        var second = result.Collections[0].Entries[1]!;
        Assert.False(second.Fields.ContainsKey("Borrower"));
    }

    [Fact]
    public void Parse_MusicXml_MapsListsAndTables()
    {
        var result = ParseText(TellicoFixtureBuilder.MusicXml());
        var collection = Assert.Single(result.Collections);

        Assert.Equal("12", result.SyntaxVersion);
        var entry = collection.Entries[0]!;
        var artists = Assert.IsType<List<string>>(entry.Fields["artists"]);
        Assert.Equal(["Joni Mitchell"], artists);

        var tracks = Assert.IsAssignableFrom<System.Collections.Generic.List<object>>(entry.Fields["tracks"]);
        Assert.Equal(2, tracks.Count);
        var firstTrack = Assert.IsAssignableFrom<System.Collections.Generic.List<object>>(tracks[0]);
        Assert.Equal(["1", "A Case of You"], firstTrack);
        var secondTrack = Assert.IsAssignableFrom<System.Collections.Generic.List<object>>(tracks[1]);
        Assert.Equal(["2", "California"], secondTrack);
    }

    [Fact]
    public void Parse_EmptyEntry_IsSkippedWithReason()
    {
        var result = ParseText(
            "<tellico syntaxVersion=\"11\"><collection title=\"C\" type=\"2\">" +
            "<entry id=\"1\"/>" +
            "<entry id=\"2\"><title>Kept</title></entry></collection></tellico>");

        var collection = Assert.Single(result.Collections);
        Assert.Equal(2, collection.Entries.Count);
        Assert.Null(collection.Entries[0]);
        Assert.Equal("Kept", collection.Entries[1]!.ExtraFields["Tellico.title"]);
        Assert.True(collection.SkippedEntriesCount >= 1);
    }

    [Fact]
    public void Parse_MusicXml_AssignsProvisionalIdWithNote()
    {
        var result = ParseText(TellicoFixtureBuilder.MusicXml());
        var collection = Assert.Single(result.Collections);

        var untitled = collection.Entries[1]!;
        Assert.StartsWith("entry-", untitled.Id);
        Assert.Equal("Untitled Album", untitled.Fields["title"]);
        Assert.Contains(collection.SkippedReasons, reason => reason.Contains("provisional id"));
    }

    [Fact]
    public void Parse_Archive_ReadsDocumentAndIgnoresImageBytes()
    {
        var result = ParseBytes(TellicoFixtureBuilder.BooksArchive());

        var collection = Assert.Single(result.Collections);
        Assert.Equal(2, collection.Entries.Count);
        Assert.True(collection.Images.ContainsKey("cover1"));
    }

    [Fact]
    public void Parse_ZipSlip_ThrowsUnsafeArchive()
    {
        Assert.Throws<TellicoUnsafeArchiveException>(() => ParseBytes(TellicoFixtureBuilder.ZipSlipArchive()));
    }

    [Fact]
    public void Parse_WrongRoot_ThrowsUnsupportedSchema()
    {
        Assert.Throws<TellicoUnsupportedSchemaException>(
            () => ParseText("<catalog><entry id=\"1\" /></catalog>"));
    }

    [Fact]
    public void Parse_OldSyntaxVersion_ThrowsUnsupportedSchema()
    {
        Assert.Throws<TellicoUnsupportedSchemaException>(
            () => ParseText("<tellico syntaxVersion=\"2\"><collection title=\"C\" type=\"2\" /></tellico>"));
    }

    [Fact]
    public void Parse_NewSyntaxVersion_ProceedsWithNote()
    {
        var result = ParseText(
            "<tellico syntaxVersion=\"99\"><collection title=\"C\" type=\"2\">" +
            "<entry id=\"1\"><title>T</title></entry></collection></tellico>");

        Assert.Equal("99", result.SyntaxVersion);
        Assert.Contains(result.Collections[0].UnsupportedFields, note => note.Contains("newer"));
    }

    [Fact]
    public void Parse_EmptyCollection_ThrowsEmptyReport()
    {
        Assert.Throws<TellicoEmptyCollectionException>(
            () => ParseText("<tellico syntaxVersion=\"11\"><collection title=\"C\" type=\"2\" /></tellico>"));
    }

    [Fact]
    public void Parse_ExternalEntity_ThrowsMalformed()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE tellico [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>" +
            "<tellico syntaxVersion=\"11\"><collection title=\"C\" type=\"2\">" +
            "<entry id=\"1\"><title>&xxe;</title></entry></collection></tellico>";
        Assert.Throws<TellicoMalformedException>(() => ParseText(xml));
    }

    [Fact]
    public void Parse_EntityExpansion_ThrowsMalformed()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE tellico [<!ENTITY a \"xxxxxxxxxx\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">" +
            "<!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">]>" +
            "<tellico syntaxVersion=\"11\"><collection title=\"C\" type=\"2\">" +
            "<entry id=\"1\"><title>&c;</title></entry></collection></tellico>";
        Assert.Throws<TellicoMalformedException>(() => ParseText(xml));
    }

    [Fact]
    public void Parse_DeeplyNestedContent_ThrowsMalformed()
    {
        var builder = new StringBuilder("<tellico syntaxVersion=\"11\"><collection title=\"C\" type=\"2\">");
        for (var i = 0; i < TellicoParser.MaxDepth + 10; i++)
        {
            builder.Append("<level").Append(i).Append('>');
        }

        builder.Append("x");
        Assert.Throws<TellicoMalformedException>(() => ParseText(builder.ToString()));
    }

    [Fact]
    public void Parse_CancelledToken_ThrowsOperationCanceled()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(TellicoFixtureBuilder.BooksXml()));
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => TellicoParser.Parse(stream, source.Token));
    }
}
