using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SkyCD.Plugin.Metadata.Archives.Ace.Exceptions;

namespace SkyCD.Plugin.Metadata.Archives.Ace.Parsing;

/// <summary>The block kinds ACE uses to separate the main header from its contained headers.</summary>
internal enum AceBlockType : byte
{
    Main = 0,
    File = 1,
    Recovery = 2
}

/// <summary>
/// One header read from an ACE archive. Only the fields needed to list the archive and to extract stored
/// entries are kept; every other field is still read so its position is validated.
/// </summary>
internal sealed class AceHeader
{
    public required AceBlockType BlockType { get; init; }

    /// <summary>Gets the entry name as stored in the header.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the compression type: 0 stored, 1 ACE 1.0 LZ77, 2 ACE 2.0 blocked.</summary>
    public int Method { get; init; }

    /// <summary>Gets the compressed data size.</summary>
    public long PackSize { get; init; }

    /// <summary>Gets the unpacked entry size.</summary>
    public long UnpackedSize { get; init; }

    /// <summary>Gets the archive offset where this entry's data starts.</summary>
    public long DataOffset { get; init; }

    public bool IsDirectory { get; init; }
    public bool IsEncrypted { get; init; }
    public bool IsMultiVolume { get; init; }

    /// <summary>Gets the ACE CRC-32 stored in the header, compared against the entry data.</summary>
    public uint FileCrc { get; init; }

    public DateTimeOffset? ModifiedUtc { get; init; }
}

/// <summary>
/// Sequential ACE 1.0/2.0 header parser. Every header is independently checksummed and the archive stores
/// its file list in the headers, so the parser can list a whole archive without decompressing any data.
/// Each block starts with a 16-bit header CRC and header size, followed by the header type, flags and the
/// type-specific body; file blocks are followed by their compressed data.
/// </summary>
internal sealed class AceHeaderReader
{
    private const int SignatureLength = 7;
    private const int BlockPrefixLength = 4;
    private const int TypeAndFlagsLength = 3;

    private const byte TypeMain = 0;
    private const byte TypeFile32 = 1;
    private const byte TypeRecovery32 = 2;
    private const byte TypeFile64 = 3;
    private const byte TypeRecovery64A = 4;
    private const byte TypeRecovery64B = 5;

    private const ushort FlagAddSize = 1 << 0;
    private const ushort FlagComment = 1 << 1;
    private const ushort Flag64Bit = 1 << 2;
    private const ushort FlagNTFSecurity = 1 << 10;
    private const ushort FlagContinuedPrevious = 1 << 12;
    private const ushort FlagContinuedNext = 1 << 13;
    private const ushort FlagPassword = 1 << 14;

    private const uint AttributeDirectory = 0x10;

    /// <summary>
    /// Validates the archive signature and main header, then returns every contained header in archive
    /// order. The stream must be seekable; its position is restored before returning.
    /// </summary>
    public List<AceHeader> Read(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanSeek)
        {
            throw new ArgumentException("The ACE reader requires a seekable stream.", nameof(source));
        }

        var origin = source.Position;
        try
        {
            source.Position = 0;
            return ReadHeaders(source, cancellationToken);
        }
        finally
        {
            source.Position = origin;
        }
    }

    private List<AceHeader> ReadHeaders(Stream source, CancellationToken cancellationToken)
    {
        var headers = new List<AceHeader>();
        var mainSeen = false;

        try
        {
            while (source.Position < source.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var header = ReadBlock(source, cancellationToken);
                if (header is null)
                {
                    continue;
                }

                if (header.BlockType == AceBlockType.Main)
                {
                    if (mainSeen)
                    {
                        throw new AceArchiveFormatException("the archive has more than one main header.");
                    }

                    mainSeen = true;
                }
                else if (!mainSeen)
                {
                    throw new AceArchiveFormatException("the archive has no ACE main header.");
                }

                headers.Add(header);
            }
        }
        catch (EndOfStreamException)
        {
            throw new AceArchiveFormatException("the archive ends inside a header or entry.");
        }

        if (!mainSeen)
        {
            throw new AceArchiveFormatException("the archive has no ACE main header.");
        }

        return headers;
    }

    /// <summary>
    /// Reads one block, verifies its checksum, skips its data and returns its header. Recovery blocks keep
    /// their data for the archiver and return a header the caller can ignore.
    /// </summary>
    private static AceHeader? ReadBlock(Stream source, CancellationToken cancellationToken)
    {
        Span<byte> prefix = stackalloc byte[BlockPrefixLength];
        var read = source.ReadAtLeast(prefix, prefix.Length, throwOnEndOfStream: false);
        if (read < prefix.Length)
        {
            throw new AceArchiveFormatException("the archive ends inside a header.");
        }

        var headerCrc = BinaryPrimitives.ReadUInt16LittleEndian(prefix);
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(prefix[2..]);
        if (headerLength < TypeAndFlagsLength)
        {
            throw new AceArchiveFormatException("a header length is smaller than the ACE minimum.");
        }

        var raw = new byte[headerLength];
        source.ReadExactly(raw);
        cancellationToken.ThrowIfCancellationRequested();

        if (AceCrc16.Compute(raw) != headerCrc)
        {
            throw new AceArchiveFormatException("a header checksum does not match.");
        }

        var type = raw[0];
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(1));
        return type switch
        {
            TypeMain => ParseMainHeader(raw, flags),
            TypeFile32 => ParseFileHeader(source, raw, flags, is64Bit: false),
            TypeFile64 => ParseFileHeader(source, raw, flags, is64Bit: true),
            TypeRecovery32 => ParseRecoveryHeader(source, raw, flags, is64Bit: false, is64B: false),
            TypeRecovery64A => ParseRecoveryHeader(source, raw, flags, is64Bit: true, is64B: false),
            TypeRecovery64B => ParseRecoveryHeader(source, raw, flags, is64Bit: true, is64B: true),
            _ => SkipUnknownBlock(source, raw, flags)
        };
    }

    private static AceHeader ParseMainHeader(ReadOnlySpan<byte> raw, ushort flags)
    {
        if ((flags & FlagAddSize) != 0)
        {
            throw new AceArchiveFormatException("the main header declares entry data.");
        }

        if (raw.Length < TypeAndFlagsLength + SignatureLength)
        {
            throw new AceArchiveFormatException("the main header ends before its signature.");
        }

        if (!raw.Slice(TypeAndFlagsLength, SignatureLength).SequenceEqual("**ACE**"u8))
        {
            throw new AceArchiveFormatException("the file does not contain the ACE signature.");
        }

        var offset = TypeAndFlagsLength + SignatureLength;
        offset += 4; // extraction version, creation version, host and volume number
        var timestamp = ReadUInt32(raw, ref offset);
        offset += 8; // reserved
        if (offset < raw.Length)
        {
            // The advert string is always present as a one-byte length followed by its bytes; the main
            // comment follows when the comment flag is set.
            offset += 1 + raw[offset];
            SkipOptionalField(raw, ref offset, flags, FlagComment);
        }

        return new AceHeader
        {
            BlockType = AceBlockType.Main,
            Name = string.Empty,
            ModifiedUtc = DecodeTimestamp(timestamp)
        };
    }

    private static AceHeader ParseFileHeader(Stream source, ReadOnlySpan<byte> raw, ushort flags, bool is64Bit)
    {
        if ((flags & FlagAddSize) == 0)
        {
            throw new AceArchiveFormatException("a file header has no data size.");
        }

        if (((flags & Flag64Bit) != 0) != is64Bit)
        {
            throw new AceArchiveFormatException("a file header uses the wrong size layout.");
        }

        var offset = TypeAndFlagsLength;
        var packSize = is64Bit ? ReadUInt64(raw, ref offset) : ReadUInt32(raw, ref offset);
        var unpackedSize = is64Bit ? ReadUInt64(raw, ref offset) : ReadUInt32(raw, ref offset);
        var timestamp = ReadUInt32(raw, ref offset);
        offset += 4; // file attributes
        var crc = ReadUInt32(raw, ref offset);
        var method = raw[offset];
        offset += 2; // compression type and quality
        offset += 2; // decompression parameters
        offset += 2; // reserved
        var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
        offset += 2;

        if (nameLength > raw.Length - offset)
        {
            throw new AceArchiveFormatException("a file header ends inside its name.");
        }

        var name = AceNameEncoding.Decode(raw.Slice(offset, nameLength));
        offset += nameLength;
        SkipOptionalField(raw, ref offset, flags, FlagComment);

        if ((flags & FlagNTFSecurity) != 0)
        {
            offset += 2 + BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
        }

        if (packSize > (ulong)(source.Length - source.Position))
        {
            throw new AceArchiveFormatException("a file header declares more data than the archive holds.");
        }

        var dataOffset = source.Position;
        source.Position += (long)packSize;

        return new AceHeader
        {
            BlockType = AceBlockType.File,
            Name = name,
            Method = method,
            PackSize = (long)packSize,
            UnpackedSize = (long)unpackedSize,
            DataOffset = dataOffset,
            IsDirectory = IsDirectory(raw, is64Bit),
            IsEncrypted = (flags & FlagPassword) != 0,
            IsMultiVolume = (flags & (FlagContinuedPrevious | FlagContinuedNext)) != 0,
            FileCrc = crc,
            ModifiedUtc = DecodeTimestamp(timestamp)
        };
    }

    private static AceHeader ParseRecoveryHeader(Stream source, ReadOnlySpan<byte> raw, ushort flags,
        bool is64Bit, bool is64B)
    {
        if ((flags & FlagAddSize) == 0)
        {
            throw new AceArchiveFormatException("a recovery header has no data size.");
        }

        if (((flags & Flag64Bit) != 0) != is64Bit)
        {
            throw new AceArchiveFormatException("a recovery header uses the wrong size layout.");
        }

        var offset = TypeAndFlagsLength;
        var recoverySize = is64Bit ? ReadUInt64(raw, ref offset) : ReadUInt32(raw, ref offset);
        offset += SignatureLength;
        offset += is64Bit ? 8 : 4; // relative start
        offset += is64B ? 8 : 10; // cluster map or cluster, cluster size and recovery CRC

        if (recoverySize > (ulong)(source.Length - source.Position))
        {
            throw new AceArchiveFormatException("a recovery header declares more data than the archive holds.");
        }

        source.Position += (long)recoverySize;

        return new AceHeader
        {
            BlockType = AceBlockType.Recovery,
            Name = string.Empty
        };
    }

    private static AceHeader? SkipUnknownBlock(Stream source, ReadOnlySpan<byte> raw, ushort flags)
    {
        if ((flags & FlagAddSize) == 0)
        {
            throw new AceUnsupportedBlockException(string.Empty, "block type", raw[0]);
        }

        var offset = TypeAndFlagsLength;
        var length = (flags & Flag64Bit) != 0 ? ReadUInt64(raw, ref offset) : ReadUInt32(raw, ref offset);
        if (length > (ulong)(source.Length - source.Position))
        {
            throw new AceArchiveFormatException("a block declares more data than the archive holds.");
        }

        source.Position += (long)length;
        return null;
    }

    private static bool IsDirectory(ReadOnlySpan<byte> raw, bool is64Bit)
    {
        // The attributes follow the two sizes and the entry timestamp.
        var attributesOffset = TypeAndFlagsLength + (is64Bit ? 16 : 8) + 4;
        if (attributesOffset + 4 > raw.Length)
        {
            return false;
        }

        var attributes = BinaryPrimitives.ReadUInt32LittleEndian(raw[attributesOffset..]);
        return (attributes & AttributeDirectory) != 0;
    }

    private static void SkipOptionalField(ReadOnlySpan<byte> raw, ref int offset, ushort flags, ushort flag)
    {
        if ((flags & flag) == 0)
        {
            return;
        }

        if (offset + 2 > raw.Length)
        {
            throw new AceArchiveFormatException("an optional header field is truncated.");
        }

        var length = BinaryPrimitives.ReadUInt16LittleEndian(raw[offset..]);
        offset += 2 + length;
        if (offset > raw.Length)
        {
            throw new AceArchiveFormatException("an optional header field ends outside its header.");
        }
    }

    private static DateTimeOffset? DecodeTimestamp(uint value)
    {
        var date = (int)(value >> 16);
        var time = (int)(value & 0xFFFF);
        var year = 1980 + ((date >> 9) & 0x7F);
        var month = (date >> 5) & 0x0F;
        var day = date & 0x1F;
        var hour = (time >> 11) & 0x1F;
        var minute = (time >> 5) & 0x3F;
        var second = (time & 0x1F) * 2;

        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc),
                TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> raw, ref int offset)
    {
        if (offset + 4 > raw.Length)
        {
            throw new AceArchiveFormatException("a header ends before a size field.");
        }

        var value = BinaryPrimitives.ReadUInt32LittleEndian(raw[offset..]);
        offset += 4;
        return value;
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> raw, ref int offset)
    {
        if (offset + 8 > raw.Length)
        {
            throw new AceArchiveFormatException("a header ends before a size field.");
        }

        var value = BinaryPrimitives.ReadUInt64LittleEndian(raw[offset..]);
        offset += 8;
        return value;
    }
}
