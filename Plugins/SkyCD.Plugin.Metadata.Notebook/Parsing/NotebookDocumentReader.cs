using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using OfficeIMO.Reader;
using OfficeIMO.Reader.Notebook;
using SkyCD.Plugin.Metadata.Notebook.Models;

namespace SkyCD.Plugin.Metadata.Notebook.Parsing;

/// <summary>
/// Reads a notebook through the bounded OfficeIMO reader and collects the facts the reader exposes:
/// notebook format version, kernel language, cell counts and the first Markdown heading of each leading
/// Markdown cell.
/// </summary>
internal static class NotebookDocumentReader
{
    private const string InvalidJsonCode = "invalid-json";
    private const string NotANotebookCode = "not-a-notebook";
    private const string LegacyNbFormatCode = "legacy-nbformat-unsupported";
    private const string EmptyCode = "empty";

    private const string NbFormatMetadataId = "notebook-nbformat";
    private const string NbFormatMinorMetadataId = "notebook-nbformat-minor";
    private const string LanguageMetadataId = "notebook-language";
    private const string CellCountMetadataId = "notebook-cell-count";
    private const string IncludedCellCountMetadataId = "notebook-included-cell-count";

    private const string MarkdownCellKind = "notebook-markdown-cell";
    private const string NotebookSourceName = "notebook.ipynb";

    public static NotebookFacts Read(byte[] bytes, NotebookPluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        if (bytes.Length == 0)
        {
            return Invalid(EmptyCode, null, null, byteLength: 0);
        }

        OfficeDocumentReadResult? document;
        try
        {
            document = ReadDocument(bytes, options, cancellationToken);
        }
        catch (JsonException exception)
        {
            return Invalid(
                InvalidJsonCode,
                ToPosition(exception.LineNumber),
                ToPosition(exception.BytePositionInLine),
                bytes.Length);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (document is null)
        {
            return Invalid(
                ContainsLegacyWorksheets(bytes) ? LegacyNbFormatCode : NotANotebookCode,
                null,
                null,
                bytes.Length);
        }

        return new NotebookFacts
        {
            Valid = true,
            ByteLength = bytes.Length,
            NbFormatVersion = ReadFormatVersion(document),
            Language = ReadText(document, LanguageMetadataId),
            CellCount = ReadInteger(document, CellCountMetadataId),
            IncludedCellCount = ReadInteger(document, IncludedCellCountMetadataId),
            CellTitles = ReadCellTitles(document, options, cancellationToken)
        };
    }

    /// <summary>Returns the parsed document, or <c>null</c> when the reader rejects the structure.</summary>
    private static OfficeDocumentReadResult? ReadDocument(
        byte[] bytes,
        NotebookPluginOptions options,
        CancellationToken cancellationToken)
    {
        var reader = new OfficeDocumentReaderBuilder()
            .AddNotebookHandler(new ReaderNotebookOptions
            {
                IncludeCodeCells = true,
                IncludeOutputs = false,
                MaxCells = options.MaxCells,
                MaxCellCharacters = options.MaxCellCharacters
            })
            .Build();

        try
        {
            return reader.ReadDocument(
                bytes,
                NotebookSourceName,
                new ReaderOptions
                {
                    ComputeHashes = false,
                    MaxInputBytes = options.MaxSourceBytes
                },
                cancellationToken);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? ReadFormatVersion(OfficeDocumentReadResult document)
    {
        var major = ReadInteger(document, NbFormatMetadataId);
        if (major is not { } version)
        {
            return null;
        }

        var minor = ReadInteger(document, NbFormatMinorMetadataId);
        return minor is { } minorVersion
            ? version.ToString(CultureInfo.InvariantCulture) + "." + minorVersion.ToString(CultureInfo.InvariantCulture)
            : version.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> ReadCellTitles(
        OfficeDocumentReadResult document,
        NotebookPluginOptions options,
        CancellationToken cancellationToken)
    {
        var titles = new List<string>(options.MaxCellTitles);
        var seenCells = new HashSet<int>();
        foreach (var chunk in document.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = chunk.Location;
            if (location is null ||
                !string.Equals(location.SourceBlockKind, MarkdownCellKind, StringComparison.Ordinal) ||
                location.SourceBlockIndex is not { } cellIndex ||
                !seenCells.Add(cellIndex))
            {
                continue;
            }

            var title = ReadHeading(chunk.Text);
            if (title is null)
            {
                continue;
            }

            titles.Add(title.Length > options.MaxTitleCharacters ? title[..options.MaxTitleCharacters] : title);
            if (titles.Count >= options.MaxCellTitles)
            {
                break;
            }
        }

        return titles;
    }

    /// <summary>Returns the first ATX heading of a Markdown cell, ignoring fenced code.</summary>
    private static string? ReadHeading(string source)
    {
        var inFence = false;
        foreach (var rawLine in source.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                continue;
            }

            var hashes = 0;
            while (hashes < line.Length && line[hashes] == '#')
            {
                hashes++;
            }

            if (hashes is >= 1 and <= 6 && (hashes == line.Length || line[hashes] == ' '))
            {
                var title = line[hashes..].Trim();
                return title.Length == 0 ? null : title;
            }
        }

        return null;
    }

    private static string? ReadText(OfficeDocumentReadResult document, string id)
    {
        foreach (var entry in document.Metadata)
        {
            if (string.Equals(entry.Id, id, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(entry.Value))
            {
                return entry.Value;
            }
        }

        return null;
    }

    private static int? ReadInteger(OfficeDocumentReadResult document, string id)
    {
        var text = ReadText(document, id);
        return text is not null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static bool ContainsLegacyWorksheets(byte[] bytes)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 128 });
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("worksheets", out var worksheets) &&
                worksheets.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int? ToPosition(long? zeroBasedPosition) =>
        zeroBasedPosition is { } value ? checked((int)value + 1) : null;

    private static NotebookFacts Invalid(string code, int? line, int? column, long byteLength) =>
        new()
        {
            Valid = false,
            ByteLength = byteLength,
            ErrorCode = code,
            ErrorLine = line,
            ErrorColumn = column
        };
}
