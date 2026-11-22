using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.Metadata.Pdf.Tests;

/// <summary>
/// Builds structurally valid PDF documents in memory — real object table, cross-reference table and trailer —
/// so the suite commits no binary blobs and every expected value is known to the test asserting it.
/// </summary>
internal static class PdfFixtures
{
    internal const int DefaultPageWidth = 612;
    internal const int DefaultPageHeight = 792;

    /// <summary>A single-page document carrying a text layer and, optionally, an information dictionary.</summary>
    internal static byte[] Document(
        int pageCount = 1,
        string? text = "Hello PDF",
        string? title = null,
        string? author = null,
        string? subject = null,
        string? keywords = null,
        string? creator = null,
        string? producer = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? modifiedAt = null,
        int pageWidth = DefaultPageWidth,
        int pageHeight = DefaultPageHeight,
        bool encrypted = false,
        string version = "1.7")
    {
        var hasText = text is not null;
        var infoEntries = BuildInfoEntries(title, author, subject, keywords, creator, producer, createdAt, modifiedAt);

        // Fixed object numbering: catalog, pages, font, then pages and their content streams, then info and
        // encryption dictionaries. Everything is numbered before anything is written so references resolve.
        const int catalogNumber = 1;
        const int pagesNumber = 2;
        const int fontNumber = 3;
        var firstPageNumber = fontNumber + 1;
        var contentCount = hasText ? pageCount : 0;
        var firstContentNumber = firstPageNumber + pageCount;
        var lastContentNumber = firstContentNumber + contentCount - 1;
        var infoNumber = infoEntries.Count > 0 ? lastContentNumber + 1 : 0;
        var encryptNumber = encrypted ? (infoNumber > 0 ? infoNumber : lastContentNumber) + 1 : 0;
        var objectCount = encryptNumber > 0 ? encryptNumber : infoNumber > 0 ? infoNumber : lastContentNumber;

        var bodies = new Dictionary<int, string>();
        bodies[catalogNumber] = "<< /Type /Catalog /Pages " + pagesNumber + " 0 R >>";

        var kids = new StringBuilder();
        for (var page = 0; page < pageCount; page++)
        {
            if (page > 0)
            {
                kids.Append(' ');
            }

            kids.Append(firstPageNumber + page).Append(" 0 R");
        }

        bodies[pagesNumber] = $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>";
        bodies[fontNumber] = "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

        for (var page = 0; page < pageCount; page++)
        {
            var number = firstPageNumber + page;
            var contentReference = hasText ? $" /Contents {firstContentNumber + page} 0 R" : string.Empty;
            bodies[number] =
                $"<< /Type /Page /Parent {pagesNumber} 0 R /MediaBox [0 0 {pageWidth} {pageHeight}]" +
                $" /Resources << /Font << /F1 {fontNumber} 0 R >> >>{contentReference} >>";
        }

        if (hasText)
        {
            var pageText = text!;
            for (var page = 0; page < pageCount; page++)
            {
                var stream = $"BT /F1 24 Tf 72 {pageHeight - 96 - (page * 4)} Td ({Escape(pageText)}) Tj ET";
                var bytes = Encoding.ASCII.GetByteCount(stream);
                bodies[firstContentNumber + page] =
                    $"<< /Length {bytes} >>\nstream\n{stream}\nendstream";
            }
        }

        if (infoNumber > 0)
        {
            bodies[infoNumber] = "<< " + string.Join(" ", infoEntries) + " >>";
        }

        if (encryptNumber > 0)
        {
            // A standard security handler with placeholder owner and user values: the point is that the
            // document declares itself protected, not that the credential material is real.
            var placeholder = new string('0', 64);
            bodies[encryptNumber] =
                $"<< /Filter /Standard /V 1 /R 2 /Length 40 /P -44 /O <{placeholder}> /U <{placeholder}> >>";
        }

        return Serialize(bodies, objectCount, catalogNumber, infoNumber, encryptNumber, version);
    }

    /// <summary>A document whose page has no content stream at all, so there is no text layer to extract.</summary>
    internal static byte[] Textless(int pageCount = 2) => Document(pageCount: pageCount, text: null);

    /// <summary>A document with enough text on one page to exceed a small excerpt bound.</summary>
    internal static byte[] LongText(int characterCount) =>
        Document(pageCount: 1, text: new string('A', characterCount));

    /// <summary>Bytes that are not a PDF in any respect.</summary>
    internal static byte[] NotPdf(int length = 64)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i * 7 + 3) % 251);
        }

        return bytes;
    }

    /// <summary>
    /// A PDF header followed by nonsense: the magic check passes, so this exercises the parser rather than
    /// the header guard.
    /// </summary>
    internal static byte[] Malformed()
    {
        var bytes = new byte[256];
        Encoding.ASCII.GetBytes("%PDF-1.7\n").CopyTo(bytes, 0);
        for (var i = 9; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((i * 13) % 251);
        }

        return bytes;
    }

    /// <summary>A valid document cut short mid cross-reference table.</summary>
    internal static byte[] Truncated(byte[] document) => document[..(document.Length / 2)];

    private static List<string> BuildInfoEntries(
        string? title,
        string? author,
        string? subject,
        string? keywords,
        string? creator,
        string? producer,
        DateTimeOffset? createdAt,
        DateTimeOffset? modifiedAt)
    {
        var entries = new List<string>();
        Add(entries, "Title", title);
        Add(entries, "Author", author);
        Add(entries, "Subject", subject);
        Add(entries, "Keywords", keywords);
        Add(entries, "Creator", creator);
        Add(entries, "Producer", producer);
        Add(entries, "CreationDate", FormatPdfDate(createdAt));
        Add(entries, "ModDate", FormatPdfDate(modifiedAt));
        return entries;

        static void Add(List<string> target, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                target.Add($"/{key} ({Escape(value)})");
            }
        }
    }

    /// <summary>PDF dates are <c>D:YYYYMMDDHHmmSS+HH'mm'</c>, independent of any host culture.</summary>
    private static string? FormatPdfDate(DateTimeOffset? value)
    {
        if (value is null)
        {
            return null;
        }

        var offset = value.Value.Offset;
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var magnitude = offset.Duration();
        return $"D:{value.Value:yyyyMMddHHmmss}{sign}{magnitude.Hours:00}'{magnitude.Minutes:00}'";
    }

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal);

    private static byte[] Serialize(
        IReadOnlyDictionary<int, string> bodies,
        int objectCount,
        int catalogNumber,
        int infoNumber,
        int encryptNumber,
        string version)
    {
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);

        writer.Write($"%PDF-{version}\n");
        writer.Flush();

        var offsets = new Dictionary<int, long>();
        for (var number = 1; number <= objectCount; number++)
        {
            if (!bodies.TryGetValue(number, out var body))
            {
                continue;
            }

            offsets[number] = stream.Position;
            writer.Write($"{number} 0 obj\n{body}\nendobj\n");
            writer.Flush();
        }

        var xrefOffset = stream.Position;
        writer.Write($"xref\n0 {objectCount + 1}\n");
        writer.Write("0000000000 65535 f \n");
        for (var number = 1; number <= objectCount; number++)
        {
            var offset = offsets.TryGetValue(number, out var found) ? found : 0;
            writer.Write(string.Format(CultureInfo.InvariantCulture, "{0:0000000000} 00000 n \n", offset));
        }

        var trailer = new StringBuilder($"trailer\n<< /Size {objectCount + 1} /Root {catalogNumber} 0 R");
        if (infoNumber > 0)
        {
            trailer.Append($" /Info {infoNumber} 0 R");
        }

        if (encryptNumber > 0)
        {
            trailer.Append($" /Encrypt {encryptNumber} 0 R");
        }

        trailer.Append(" >>\n");
        writer.Write(trailer.ToString());
        writer.Write($"startxref\n{xrefOffset}\n%%EOF\n");
        writer.Flush();

        return stream.ToArray();
    }
}
