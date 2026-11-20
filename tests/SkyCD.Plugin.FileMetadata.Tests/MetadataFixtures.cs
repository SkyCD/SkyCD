using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.FileMetadata.Tests;

/// <summary>
/// Builds byte-accurate fixtures in memory so every reader is exercised against the real container layout
/// rather than a mock of the parser under test.
/// </summary>
internal static class MetadataFixtures
{
    public static string Xmp(
        IReadOnlyList<string>? keywords = null,
        IReadOnlyList<string>? hierarchical = null,
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
        builder.Append(" xmlns:lr=\"http://ns.adobe.com/lightroom/1.0/\"");
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

        if (hierarchical is not null)
        {
            builder.Append("<lr:hierarchicalSubject><rdf:Bag>");
            foreach (var path in hierarchical)
            {
                builder.Append("<rdf:li>").Append(Escape(path)).Append("</rdf:li>");
            }

            builder.Append("</rdf:Bag></lr:hierarchicalSubject>");
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

    public static byte[] Jpeg(string? xmp = null, string? comment = null, byte[]? iim = null)
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]);

        if (xmp is not null)
        {
            var payload = Concat(Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0"),
                Encoding.UTF8.GetBytes(xmp));
            WriteSegment(stream, 0xE1, payload);
        }

        if (iim is not null)
        {
            WriteSegment(stream, 0xED, PhotoshopResource(iim));
        }

        if (comment is not null)
        {
            WriteSegment(stream, 0xFE, Encoding.Latin1.GetBytes(comment));
        }

        // Minimal Start-Of-Scan followed by a byte of entropy data and End-Of-Image.
        WriteSegment(stream, 0xDA, [0x01, 0x01, 0x00]);
        stream.Write([0x00, 0xFF, 0xD9]);
        return stream.ToArray();
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

    public static byte[] PhotoshopResource(byte[] iim)
    {
        using var block = new MemoryStream();
        block.Write(Encoding.ASCII.GetBytes("8BIM"));
        block.WriteByte(0x04);
        block.WriteByte(0x04);
        block.WriteByte(0x00);
        block.WriteByte(0x00);
        WriteUInt32BigEndian(block, (uint)iim.Length);
        block.Write(iim);
        if ((iim.Length & 1) == 1)
        {
            block.WriteByte(0x00);
        }

        return Concat(Encoding.ASCII.GetBytes("Photoshop 3.0\0"), block.ToArray());
    }

    public static byte[] Png(
        IReadOnlyList<(string Keyword, string Text)>? text = null,
        string? xmp = null)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(stream, "IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]);
        if (text is not null)
        {
            foreach (var (keyword, value) in text)
            {
                WriteChunk(stream, "tEXt", Concat(Encoding.Latin1.GetBytes(keyword + "\0"),
                    Encoding.Latin1.GetBytes(value)));
            }
        }

        if (xmp is not null)
        {
            WriteChunk(stream, "iTXt", Concat(Encoding.ASCII.GetBytes("XML:com.adobe.xmp\0\0\0\0\0"),
                Encoding.UTF8.GetBytes(xmp)));
        }

        WriteChunk(stream, "IEND", []);
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

    public static byte[] Tiff(bool littleEndian, params TiffEntry[] entries)
    {
        using var stream = new MemoryStream();
        stream.Write(littleEndian ? "II"u8 : "MM"u8);
        WriteUInt16(stream, 42, littleEndian);
        WriteUInt32(stream, 8, littleEndian);
        WriteUInt16(stream, (ushort)entries.Length, littleEndian);

        var dataStart = 8 + 2 + entries.Length * 12 + 4;
        var data = new MemoryStream();
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
        data.Position = 0;
        data.CopyTo(stream);
        return stream.ToArray();
    }

    private static byte[] Concat(byte[] first, byte[] second) => [.. first, .. second];

    private static void WriteSegment(Stream stream, byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        stream.WriteByte(0xFF);
        stream.WriteByte(marker);
        stream.WriteByte((byte)(length >> 8));
        stream.WriteByte((byte)(length & 0xFF));
        stream.Write(payload);
    }

    private static void WriteChunk(Stream stream, string type, byte[] payload)
    {
        WriteUInt32BigEndian(stream, (uint)payload.Length);
        stream.Write(Encoding.ASCII.GetBytes(type));
        stream.Write(payload);
        stream.Write([0, 0, 0, 0]);
    }

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
            WriteUInt32BigEndian(stream, value);
        }
    }

    private static void WriteUInt32BigEndian(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
