using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.FileFormat.Gzip.Parsing;

internal enum GzipHeaderStatus
{
    Valid,
    Damaged,
    Unsupported
}

/// <summary>
/// The fixed gzip member header (RFC 1952) plus the optional original file name. The host indexes a
/// single member, so only the first header is read; trailing concatenated members are ignored.
/// </summary>
internal sealed record GzipMemberHeader(GzipHeaderStatus Status, string? OriginalName, DateTimeOffset? ModifiedUtc)
{
    private const int FixedLength = 10;
    private const int TrailerLength = 8;

    /// <summary>Gets the smallest complete member: fixed header plus an empty deflate block and trailer.</summary>
    public const int MinimumLength = FixedLength + TrailerLength;
    private const byte DeflateMethod = 8;
    private const byte FlagFileName = 0x08;
    private const byte ReservedFlags = 0xE0;
    private const int NameLimit = 1024;

    public static GzipMemberHeader Damaged { get; } = new(GzipHeaderStatus.Damaged, null, null);

    public static GzipMemberHeader Unsupported { get; } = new(GzipHeaderStatus.Unsupported, null, null);

    public static GzipMemberHeader Read(Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanSeek)
        {
            throw new ArgumentException("The gzip header requires a seekable stream.", nameof(source));
        }

        var origin = source.Position;
        try
        {
            source.Position = 0;
            Span<byte> fixedHeader = stackalloc byte[FixedLength];
            if (source.ReadAtLeast(fixedHeader, fixedHeader.Length, throwOnEndOfStream: false) < fixedHeader.Length
                || fixedHeader[0] != 0x1F
                || fixedHeader[1] != 0x8B)
            {
                return Damaged;
            }

            var flags = fixedHeader[3];
            if ((flags & ReservedFlags) != 0)
            {
                return Damaged;
            }

            if (fixedHeader[2] != DeflateMethod)
            {
                return Unsupported;
            }

            string? name = null;
            if ((flags & FlagFileName) != 0)
            {
                if (!TryReadName(source, out name))
                {
                    return Damaged;
                }
            }

            return new GzipMemberHeader(GzipHeaderStatus.Valid, name, ReadModified(fixedHeader[4..8]));
        }
        finally
        {
            source.Position = origin;
        }
    }

    /// <summary>Reads the uncompressed size stored in the member trailer; null when unavailable.</summary>
    public static long? TryReadUncompressedSize(Stream source)
    {
        if (!source.CanSeek)
        {
            return null;
        }

        var origin = source.Position;
        try
        {
            if (source.Length < MinimumLength)
            {
                return null;
            }

            source.Position = source.Length - 4;
            Span<byte> isize = stackalloc byte[4];
            if (source.ReadAtLeast(isize, isize.Length, throwOnEndOfStream: false) < isize.Length)
            {
                return null;
            }

            return BinaryPrimitives.ReadUInt32LittleEndian(isize);
        }
        catch (IOException)
        {
            return null;
        }
        finally
        {
            source.Position = origin;
        }
    }

    private static bool TryReadName(Stream source, out string? name)
    {
        name = null;
        var bytes = new byte[NameLimit];
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = source.ReadByte();
            if (value < 0)
            {
                return false;
            }

            if (value == 0)
            {
                name = Encoding.Latin1.GetString(bytes, 0, index);
                return true;
            }

            bytes[index] = (byte)value;
        }

        return false;
    }

    private static DateTimeOffset? ReadModified(ReadOnlySpan<byte> mtime)
    {
        var seconds = BinaryPrimitives.ReadUInt32LittleEndian(mtime);
        if (seconds == 0)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
