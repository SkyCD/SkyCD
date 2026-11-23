using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.Metadata.Image.Tests;

/// <summary>
/// Builds byte-accurate fixtures in memory so every reader is exercised against the real container layout
/// rather than a mock of the parser under test.
/// </summary>
internal static class MetadataFixtures
{
    public static string Xmp(
        IReadOnlyList<string>? keywords = null,
        string? description = null,
        int? rating = null,
        int? microsoftRating = null,
        string? pdfKeywords = null)
    {
        var builder = new StringBuilder();
        builder.Append("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        builder.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");
        builder.Append("<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        builder.Append("<rdf:Description rdf:about=\"\"");
        builder.Append(" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"");
        builder.Append(" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"");
        builder.Append(" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\"");
        builder.Append(" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\">");

        if (keywords is not null)
        {
            builder.Append("<dc:subject><rdf:Bag>");
            foreach (var keyword in keywords)
            {
                builder.Append("<rdf:li>").Append(Escape(keyword)).Append("</rdf:li>");
            }

            builder.Append("</rdf:Bag></dc:subject>");
        }

        if (description is not null)
        {
            builder.Append("<dc:description><rdf:Alt>");
            builder.Append("<rdf:li xml:lang=\"x-default\">").Append(Escape(description)).Append("</rdf:li>");
            builder.Append("</rdf:Alt></dc:description>");
        }

        if (rating is not null)
        {
            builder.Append("<xmp:Rating>").Append(rating.Value).Append("</xmp:Rating>");
        }

        if (microsoftRating is not null)
        {
            builder.Append("<MicrosoftPhoto:Rating>").Append(microsoftRating.Value)
                .Append("</MicrosoftPhoto:Rating>");
        }

        if (pdfKeywords is not null)
        {
            builder.Append("<pdf:Keywords>").Append(Escape(pdfKeywords)).Append("</pdf:Keywords>");
        }

        builder.Append("</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>");
        return builder.ToString();
    }

    public static byte[] Iim(params (byte Dataset, string Value)[] records)
    {
        using var stream = new MemoryStream();
        foreach (var (dataset, value) in records)
        {
            var bytes = Encoding.Latin1.GetBytes(value);
            stream.Write([0x1C, 0x02, dataset]);
            stream.WriteByte((byte)(bytes.Length >> 8));
            stream.WriteByte((byte)(bytes.Length & 0xFF));
            stream.Write(bytes);
        }

        return stream.ToArray();
    }

    public sealed record TiffEntry(ushort Tag, ushort Type, uint Count, byte[] Value);

    public static TiffEntry Ascii(ushort tag, string value)
    {
        var bytes = Encoding.Latin1.GetBytes(value + "\0");
        return new TiffEntry(tag, 2, (uint)bytes.Length, bytes);
    }

    public static TiffEntry Utf16(ushort tag, string value, bool littleEndian)
    {
        var bytes = littleEndian
            ? Encoding.Unicode.GetBytes(value)
            : Encoding.BigEndianUnicode.GetBytes(value);
        return new TiffEntry(tag, 1, (uint)bytes.Length, bytes);
    }

    public static TiffEntry Short(ushort tag, ushort value, bool littleEndian)
    {
        var bytes = littleEndian ? new[] { (byte)value, (byte)(value >> 8) }
            : new[] { (byte)(value >> 8), (byte)value };
        return new TiffEntry(tag, 3, 1, bytes);
    }

    /// <summary>A BYTE or UNDEFINED payload, as TIFF-based RAW tools store IPTC-IIM and XMP packets.</summary>
    public static TiffEntry Raw(ushort tag, byte[] value, ushort type = 1) =>
        new(tag, type, (uint)value.Length, value);

    public static byte[] Tiff(bool littleEndian, params TiffEntry[] entries) =>
        BuildTiff(littleEndian, entries, subIfdEntries: null);

    /// <summary>A TIFF whose IFD0 points at an Exif sub-IFD holding the original timestamp tags.</summary>
    public static byte[] TiffWithSubIfd(bool littleEndian, TiffEntry[] ifd0, TiffEntry[] subIfd) =>
        BuildTiff(littleEndian, ifd0, subIfd);

    private static byte[] BuildTiff(bool littleEndian, TiffEntry[] ifd0Entries, TiffEntry[]? subIfdEntries)
    {
        const ushort ExifSubIfdPointer = 0x8769;

        var ifd0Start = 8;
        var pointerEntries = subIfdEntries is null ? 0 : 1;
        var ifd0Size = 2 + (((ifd0Entries.Length + pointerEntries) * 12)) + 4;
        var subIfdStart = ifd0Start + ifd0Size;
        var subIfdSize = subIfdEntries is null ? 0 : 2 + (subIfdEntries.Length * 12) + 4;
        var dataStart = subIfdStart + subIfdSize;

        var ifd0 = subIfdEntries is null
            ? ifd0Entries
            : [.. ifd0Entries,
                new TiffEntry(ExifSubIfdPointer, 4, 1, LongBytes((uint)subIfdStart, littleEndian))];

        using var stream = new MemoryStream();
        stream.Write(littleEndian ? "II"u8 : "MM"u8);
        WriteUInt16(stream, 42, littleEndian);
        WriteUInt32(stream, (uint)ifd0Start, littleEndian);

        var data = new MemoryStream();
        WriteDirectory(stream, ifd0, data, dataStart, littleEndian);
        if (subIfdEntries is not null)
        {
            WriteDirectory(stream, subIfdEntries, data, dataStart, littleEndian);
        }

        data.Position = 0;
        data.CopyTo(stream);
        return stream.ToArray();
    }

    private static void WriteDirectory(Stream stream, IReadOnlyList<TiffEntry> entries, MemoryStream data,
        int dataStart, bool littleEndian)
    {
        WriteUInt16(stream, (ushort)entries.Count, littleEndian);
        foreach (var entry in entries)
        {
            WriteUInt16(stream, entry.Tag, littleEndian);
            WriteUInt16(stream, entry.Type, littleEndian);
            WriteUInt32(stream, entry.Count, littleEndian);
            if (entry.Value.Length <= 4)
            {
                var inline = new byte[4];
                entry.Value.CopyTo(inline, 0);
                stream.Write(inline);
            }
            else
            {
                WriteUInt32(stream, (uint)(dataStart + data.Length), littleEndian);
                data.Write(entry.Value);
                if ((entry.Value.Length & 1) == 1)
                {
                    data.WriteByte(0);
                }
            }
        }

        WriteUInt32(stream, 0, littleEndian);
    }

    private static byte[] LongBytes(uint value, bool littleEndian) => littleEndian
        ? [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)]
        : [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static void WriteUInt16(Stream stream, ushort value, bool littleEndian)
    {
        if (littleEndian)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }
        else
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }
    }

    private static void WriteUInt32(Stream stream, uint value, bool littleEndian)
    {
        if (littleEndian)
        {
            for (var shift = 0; shift < 32; shift += 8)
            {
                stream.WriteByte((byte)(value >> shift));
            }
        }
        else
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
