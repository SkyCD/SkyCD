using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.Fonts.Exceptions;

namespace SkyCD.Plugin.Fonts;

/// <summary>Name facts of one face, already decoded and trimmed.</summary>
internal sealed record FontFace(
    string? Family,
    string? Subfamily,
    string? FullName,
    string? PostScriptName,
    string? Version,
    string? Copyright);

/// <summary>
/// Reads the sfnt directory and the <c>name</c> table of single fonts and ttc/otc collections. Only the
/// directory and the name table are touched, so a damaged glyph, kerning or CFF table never prevents metadata
/// collection and a hostile offset cannot make the parser read outside the source.
/// </summary>
internal static class FontFile
{
    private const uint SfntTrueType = 0x00010000;
    private const uint SfntOpenType = 0x4F54544F; // 'OTTO'
    private const uint SfntTrue = 0x74727565;     // 'true'
    private const uint SfntType1 = 0x74797031;    // 'typ1'
    private const uint Collection = 0x74746366;   // 'ttcf'
    private const uint NameTable = 0x6E616D65;    // 'name'

    private const int SfntHeaderBytes = 12;
    private const int TableRecordBytes = 16;
    private const int CollectionHeaderBytes = 12;
    private const int NameHeaderBytes = 6;
    private const int NameRecordBytes = 12;
    private const int MaxNameRecords = 4096;

    public static IReadOnlyList<FontFace> ReadFaces(Stream stream, FontPluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);

        var length = stream.Length;
        if (length < SfntHeaderBytes)
        {
            throw new FontFormatException("the file is too short to be a font");
        }

        var header = ReadExactly(stream, 0, SfntHeaderBytes, cancellationToken);
        var version = BinaryPrimitives.ReadUInt32BigEndian(header);
        var faceOffsets = version == Collection
            ? ReadCollectionOffsets(stream, header, length, options, cancellationToken)
            : [0L];

        var faces = new List<FontFace>();
        foreach (var faceOffset in faceOffsets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var face = TryReadFace(stream, faceOffset, length, options, cancellationToken);
            if (face is not null)
            {
                faces.Add(face);
            }
        }

        if (faces.Count == 0)
        {
            throw new FontFormatException("no readable name table was found");
        }

        return faces;
    }

    private static List<long> ReadCollectionOffsets(Stream stream, byte[] header, long length,
        FontPluginOptions options, CancellationToken cancellationToken)
    {
        var count = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8, 4));
        var cap = Math.Min(count, (uint)options.MaxFaces);
        var offsets = new List<long>((int)cap);

        for (var index = 0; index < cap; index++)
        {
            var recordOffset = CollectionHeaderBytes + (long)index * 4;
            if (recordOffset + 4 > length)
            {
                break;
            }

            var faceOffset = BinaryPrimitives.ReadUInt32BigEndian(
                ReadExactly(stream, recordOffset, 4, cancellationToken));
            if (faceOffset + SfntHeaderBytes <= length)
            {
                offsets.Add(faceOffset);
            }
        }

        return offsets;
    }

    private static FontFace? TryReadFace(Stream stream, long faceOffset, long length, FontPluginOptions options,
        CancellationToken cancellationToken)
    {
        if (faceOffset + SfntHeaderBytes > length)
        {
            return null;
        }

        var header = ReadExactly(stream, faceOffset, SfntHeaderBytes, cancellationToken);
        var version = BinaryPrimitives.ReadUInt32BigEndian(header);
        if (version is not (SfntTrueType or SfntOpenType or SfntTrue or SfntType1))
        {
            return null;
        }

        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
        if (tableCount == 0 || tableCount > options.MaxTables)
        {
            return null;
        }

        var directoryBytes = (long)tableCount * TableRecordBytes;
        if (faceOffset + SfntHeaderBytes + directoryBytes > length)
        {
            return null;
        }

        var directory = ReadExactly(stream, faceOffset + SfntHeaderBytes, (int)directoryBytes, cancellationToken);
        for (var index = 0; index < tableCount; index++)
        {
            var record = directory.AsSpan(index * TableRecordBytes, TableRecordBytes);
            if (BinaryPrimitives.ReadUInt32BigEndian(record) != NameTable)
            {
                continue;
            }

            var tableOffset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var tableLength = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            if (tableLength == 0 || tableLength > options.MaxNameTableBytes ||
                tableOffset > length || tableOffset + tableLength > length)
            {
                return null;
            }

            var table = ReadExactly(stream, tableOffset, (int)tableLength, cancellationToken);
            return ParseNames(table, options);
        }

        return null;
    }

    private static FontFace? ParseNames(byte[] table, FontPluginOptions options)
    {
        if (table.Length < NameHeaderBytes)
        {
            return null;
        }

        var format = BinaryPrimitives.ReadUInt16BigEndian(table);
        if (format > 1)
        {
            return null;
        }

        var count = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(2, 2));
        var stringOffset = BinaryPrimitives.ReadUInt16BigEndian(table.AsSpan(4, 2));
        if (count == 0 || stringOffset > table.Length)
        {
            return null;
        }

        var recordLimit = Math.Min((int)count, MaxNameRecords);
        if (NameHeaderBytes + recordLimit * NameRecordBytes > table.Length)
        {
            recordLimit = (table.Length - NameHeaderBytes) / NameRecordBytes;
        }

        var values = new Dictionary<ushort, (int Score, string Value)>();
        for (var index = 0; index < recordLimit; index++)
        {
            var record = table.AsSpan(NameHeaderBytes + index * NameRecordBytes, NameRecordBytes);
            var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            var language = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(record[6..]);
            var valueLength = BinaryPrimitives.ReadUInt16BigEndian(record[8..]);
            var valueOffset = BinaryPrimitives.ReadUInt16BigEndian(record[10..]);

            var start = stringOffset + valueOffset;
            if (valueLength == 0 || start > table.Length || valueLength > table.Length - start)
            {
                continue;
            }

            var score = Score(platform, language);
            if (score == 0)
            {
                continue;
            }

            if (values.TryGetValue(nameId, out var existing) && existing.Score >= score)
            {
                continue;
            }

            var text = Decode(table.AsSpan(start, valueLength), platform);
            if (!string.IsNullOrWhiteSpace(text))
            {
                values[nameId] = (score, text);
            }
        }

        var family = Pick(values, 16, 1);
        var subfamily = Pick(values, 17, 2);
        var fullName = Pick(values, 4);
        if (family is null && fullName is null)
        {
            return null;
        }

        return new FontFace(
            Trim(family, options),
            Trim(subfamily, options),
            Trim(fullName, options),
            Trim(Pick(values, 6), options),
            Trim(Pick(values, 5), options),
            Trim(Pick(values, 0), options));
    }

    private static int Score(ushort platform, ushort language) => platform switch
    {
        3 when language == 0x0409 => 3,
        3 => 2,
        0 => 2,
        1 => 1,
        _ => 0
    };

    private static string Decode(ReadOnlySpan<byte> bytes, ushort platform)
    {
        if (platform is 0 or 3)
        {
            var even = bytes.Length - bytes.Length % 2;
            return Encoding.BigEndianUnicode.GetString(bytes[..even]);
        }

        return Encoding.Latin1.GetString(bytes);
    }

    private static string? Pick(Dictionary<ushort, (int Score, string Value)> values, ushort first,
        ushort? second = null)
    {
        if (values.TryGetValue(first, out var preferred))
        {
            return preferred.Value;
        }

        return second is { } fallback && values.TryGetValue(fallback, out var value) ? value.Value : null;
    }

    private static string? Trim(string? value, FontPluginOptions options)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Replace('\0', ' ').Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.Length > options.MaxFieldCharacters ? trimmed[..options.MaxFieldCharacters] : trimmed;
    }

    private static byte[] ReadExactly(Stream stream, long offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var buffer = new byte[count];
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(buffer);
        return buffer;
    }
}
