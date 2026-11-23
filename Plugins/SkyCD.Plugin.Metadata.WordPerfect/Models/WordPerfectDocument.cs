using System;
using System.Collections.Generic;
using SkyCD.Plugin.Metadata.WordPerfect.Exceptions;

namespace SkyCD.Plugin.Metadata.WordPerfect.Models;

/// <summary>
/// Header facts and extended document summary parsed from one WordPerfect document stream.
/// </summary>
internal sealed class WordPerfectDocument
{
    private const byte WordPerfectFileType = 0x0A;
    private const byte MacWordPerfectFileType = 0x2C;
    private const byte WordPerfect6MajorVersion = 0x02;
    private const int MaxSummaryBytes = 1024 * 1024;

    private static readonly IReadOnlyDictionary<ushort, WordPerfectSummaryValue> NoSummary =
        new Dictionary<ushort, WordPerfectSummaryValue>();

    private WordPerfectDocument(string formatVersion, string storage,
        IReadOnlyDictionary<ushort, WordPerfectSummaryValue> summary, bool supportsBodyText, int documentOffset)
    {
        FormatVersion = formatVersion;
        Storage = storage;
        Summary = summary;
        SupportsBodyText = supportsBodyText;
        DocumentOffset = documentOffset;
    }

    /// <summary>Product generation read from the header, for example <c>6.0</c> or <c>6.1+</c>.</summary>
    public string FormatVersion { get; }

    /// <summary>Whether the WordPerfect stream was stored flat or inside a compound file.</summary>
    public string Storage { get; }

    /// <summary>Summary groups keyed by their WordPerfect tag; empty for WP5 and Mac WP documents.</summary>
    public IReadOnlyDictionary<ushort, WordPerfectSummaryValue> Summary { get; }

    /// <summary>True when the body stream uses the WP6 grammar this plugin can walk for leading text.</summary>
    public bool SupportsBodyText { get; }

    /// <summary>Offset of the first body byte, clamped so it never points into the header or past the end.</summary>
    public int DocumentOffset { get; }

    public static bool HasMagic(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[0] == 0xFF && data[1] == (byte)'W' && data[2] == (byte)'P' && data[3] == (byte)'C';

    public static WordPerfectDocument Parse(byte[] data, string storage)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(storage);

        if (data.Length < 16)
        {
            throw new WordPerfectDecodeException("the document header is truncated");
        }

        if (!HasMagic(data))
        {
            throw new WordPerfectDecodeException("the source does not start with a WordPerfect header");
        }

        var fileType = data[9];
        var majorVersion = data[10];
        var minorVersion = data[11];
        if (ReadU16(data, 12) != 0)
        {
            throw new WordPerfectAuthenticationRequiredException("the document header declares encryption");
        }

        var formatVersion = ResolveFormatVersion(fileType, majorVersion, minorVersion);
        var supportsBodyText = fileType == WordPerfectFileType && majorVersion == WordPerfect6MajorVersion;
        var summary = supportsBodyText ? ReadSummary(data) : NoSummary;
        var documentOffset = (int)Math.Clamp((long)ReadU32(data, 4), 16L, data.Length);

        return new WordPerfectDocument(formatVersion, storage, summary, supportsBodyText, documentOffset);
    }

    private static IReadOnlyDictionary<ushort, WordPerfectSummaryValue> ReadSummary(byte[] data)
    {
        var indexHeaderOffset = ReadU16(data, 14);
        if (indexHeaderOffset < 16)
        {
            indexHeaderOffset = 16;
        }

        if ((long)indexHeaderOffset + 14 > data.Length)
        {
            return NoSummary;
        }

        var indexCount = ReadU16(data, indexHeaderOffset + 2);
        if (indexCount <= 1)
        {
            return NoSummary;
        }

        var entryCount = indexCount - 1;
        var tableOffset = indexHeaderOffset + 14;
        if ((long)tableOffset + ((long)entryCount * 14) > data.Length)
        {
            return NoSummary;
        }

        for (var index = 0; index < entryCount; index++)
        {
            var entryOffset = tableOffset + (index * 14);
            if (data[entryOffset + 1] != 0x12)
            {
                continue;
            }

            var dataSize = ReadU32(data, entryOffset + 6);
            var dataOffset = ReadU32(data, entryOffset + 10);
            if (dataSize == 0 || dataSize > MaxSummaryBytes
                || (long)dataOffset + dataSize > data.Length)
            {
                continue;
            }

            return WordPerfectSummary.Parse(data.AsSpan((int)dataOffset, (int)dataSize));
        }

        return NoSummary;
    }

    private static string ResolveFormatVersion(byte fileType, byte majorVersion, byte minorVersion) =>
        (fileType, majorVersion) switch
        {
            (WordPerfectFileType, 0x00) => "5",
            (WordPerfectFileType, WordPerfect6MajorVersion) when minorVersion == 0 => "6.0",
            (WordPerfectFileType, WordPerfect6MajorVersion) => "6.1+",
            (MacWordPerfectFileType, 0x02) => "Mac 2.x",
            (MacWordPerfectFileType, 0x03) => "Mac 3.0-3.5",
            (MacWordPerfectFileType, 0x04) => "Mac 3.5e",
            _ => "unknown"
        };

    private static ushort ReadU16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadU32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
}
