using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Csv;
using Xunit;
using SkyCD.Plugin.Csv.Parsing;

namespace SkyCD.Plugin.Host.Tests;

/// <summary>
/// Integration tests for <see cref="CsvParser"/> after the CsvHelper migration.
/// Covers DetectDelimiter, ParseLine, ReadCsvAsync, ReadAllRowsAsync, and Escape.
/// </summary>
public sealed class CsvParserTests
{
    // ── DetectDelimiter ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("a,b,c", ',')]
    [InlineData("a;b;c", ';')]
    [InlineData("a\tb\tc", '\t')]
    [InlineData("a,b;c", ',')] // comma wins tie: more commas
    [InlineData("", ',')]       // empty → default comma
    [InlineData("no-delimiters", ',')] // no delimiters → comma
    public void DetectDelimiter_ReturnsExpected(string line, char expected)
    {
        Assert.Equal(expected, CsvParser.DetectDelimiter(line));
    }

    [Fact]
    public void DetectDelimiter_IgnoresQuotedDelimiters()
    {
        // The semicolons inside quotes should not count.
        var line = "\"a;b\",c,d";
        Assert.Equal(',', CsvParser.DetectDelimiter(line));
    }

    // ── ParseLine ────────────────────────────────────────────────────────────

    [Fact]
    public void ParseLine_SplitsSimpleCommaFields()
    {
        var result = CsvParser.ParseLine("a,b,c");
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void ParseLine_HandlesQuotedFieldWithComma()
    {
        var result = CsvParser.ParseLine("\"a,b\",c");
        Assert.Equal(["a,b", "c"], result);
    }

    [Fact]
    public void ParseLine_HandlesEscapedQuotesRfc4180()
    {
        var result = CsvParser.ParseLine("\"d\"\"e\",f");
        Assert.Equal(["d\"e", "f"], result);
    }

    [Fact]
    public void ParseLine_UsesExplicitDelimiter()
    {
        var result = CsvParser.ParseLine("a;b;c", ';');
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void ParseLine_EmptyFieldsRoundTrip()
    {
        var result = CsvParser.ParseLine(",b,");
        Assert.Equal(["", "b", ""], result);
    }

    // ── ReadCsvAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadCsvAsync_DetectsCommaDelimiter()
    {
        using var reader = new StringReader("Name,Size\na.txt,100\n");
        var result = await CsvParser.ReadCsvAsync(reader);
        Assert.Equal(',', result.Delimiter);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(["Name", "Size"], result.Rows[0]);
        Assert.Equal(["a.txt", "100"], result.Rows[1]);
    }

    [Fact]
    public async Task ReadCsvAsync_DetectsSemicolonDelimiter()
    {
        using var reader = new StringReader("Name;Size\na.txt;100\n");
        var result = await CsvParser.ReadCsvAsync(reader);
        Assert.Equal(';', result.Delimiter);
        Assert.Equal(["a.txt", "100"], result.Rows[1]);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task ReadCsvAsync_NormalizesMultilineQuotedFieldToLf(string newline)
    {
        var csv = $"Name;Desc{newline}file.txt;\"line1{newline}line2\"{newline}";
        using var reader = new StringReader(csv);
        var result = await CsvParser.ReadCsvAsync(reader);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(';', result.Delimiter);
        Assert.Equal("line1\nline2", result.Rows[1][1]);
    }

    [Fact]
    public async Task ReadCsvAsync_SkipsBlankLinesBetweenRecords()
    {
        const string csv = "a,b\n\nc,d\n\n";
        using var reader = new StringReader(csv);
        var result = await CsvParser.ReadCsvAsync(reader);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(["a", "b"], result.Rows[0]);
        Assert.Equal(["c", "d"], result.Rows[1]);
    }

    [Fact]
    public async Task ReadCsvAsync_RespectsExplicitDelimiter()
    {
        using var reader = new StringReader("a,b;c,d");
        // Force comma; the semicolons remain as literal data
        var result = await CsvParser.ReadCsvAsync(reader, ',');
        Assert.Equal(',', result.Delimiter);
        Assert.Equal(["a", "b;c", "d"], result.Rows[0]);
    }

    [Fact]
    public async Task ReadCsvAsync_HandleRfc4180EscapedQuotes()
    {
        const string csv = "Name,Desc\n\"d\"\"e\",plain\n";
        using var reader = new StringReader(csv);
        var result = await CsvParser.ReadCsvAsync(reader);
        Assert.Equal("d\"e", result.Rows[1][0]);
    }

    [Fact]
    public async Task ReadAllRowsAsync_ReturnsSameDataAsReadCsvAsync()
    {
        const string csv = "x,y\n1,2\n3,4\n";
        using var r1 = new StringReader(csv);
        using var r2 = new StringReader(csv);
        var full = await CsvParser.ReadCsvAsync(r1);
        var rows = await CsvParser.ReadAllRowsAsync(r2);
        Assert.Equal(full.Rows, rows);
    }

    [Fact]
    public async Task ReadCsvAsync_EmptyInput_ReturnsNoRows()
    {
        using var reader = new StringReader(string.Empty);
        var result = await CsvParser.ReadCsvAsync(reader);
        Assert.Empty(result.Rows);
        Assert.Equal(',', result.Delimiter);
    }

    [Fact]
    public async Task ReadCsvAsync_HonoursCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var reader = new StringReader("a,b\n");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CsvParser.ReadCsvAsync(reader, cancellationToken: cts.Token));
    }

    // ── Escape ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\nb", "\"a\nb\"")]
    [InlineData("a\rb", "\"a\rb\"")]
    public void Escape_ReturnsCorrectOutput(string? input, string expected)
    {
        Assert.Equal(expected, CsvParser.Escape(input));
    }

    [Fact]
    public void Escape_UsesExplicitDelimiter()
    {
        // Semicolon delimiter: commas are NOT quoted, semicolons are.
        Assert.Equal("\"a;b\"", CsvParser.Escape("a;b", ';'));
        Assert.Equal("a,b", CsvParser.Escape("a,b", ';'));
    }
}
