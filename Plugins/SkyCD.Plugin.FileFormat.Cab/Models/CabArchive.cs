using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.FileFormat.Cab.Exceptions;

namespace SkyCD.Plugin.FileFormat.Cab.Models;

/// <summary>One folder record from a cabinet header.</summary>
internal sealed record CabFolder(int CompressionType, int DataBlockCount, string FirstEntryName = "");

/// <summary>One member file record from a cabinet header.</summary>
internal sealed record CabFile(
    string Name,
    long Size,
    long OffsetInFolder,
    long HeaderOffset,
    ushort FolderIndex,
    DateTimeOffset ModifiedUtc);

/// <summary>Parsed cabinet header facts that are needed to list and extract members.</summary>
internal sealed class CabArchive
{
    private const int HeaderPrefixBytes = 36;
    private const int MinimumHeaderBytes = 32;
    private const int FolderRecordBytes = 8;
    private const int FileRecordFixedBytes = 16;
    private const int MaxNameBytes = 65_535;
    private const int MaxOptionalFieldBytes = 255;

    public const int TypeStored = 0;
    public const int TypeMsZip = 1;
    public const int TypeQuantum = 2;
    public const int TypeLzx = 3;

    private CabArchive(
        string? prevCabinet,
        string? nextCabinet,
        int flags,
        int folderReserveBytes,
        long cabinetStart,
        IReadOnlyList<CabFolder> folders,
        IReadOnlyList<CabFile> files)
    {
        PrevCabinet = prevCabinet;
        NextCabinet = nextCabinet;
        Flags = flags;
        FolderReserveBytes = folderReserveBytes;
        CabinetStart = cabinetStart;
        Folders = folders;
        Files = files;
    }

    /// <summary>Gets the name of the preceding cabinet in a multi-volume set, when declared.</summary>
    public string? PrevCabinet { get; }

    /// <summary>Gets the name of the following cabinet in a multi-volume set, when declared.</summary>
    public string? NextCabinet { get; }

    /// <summary>Gets the header flags.</summary>
    public int Flags { get; }

    /// <summary>Gets the per-folder reserve area size that precedes each compressed folder stream.</summary>
    public int FolderReserveBytes { get; }

    /// <summary>Gets the offset of the first CFDATA block of the first folder.</summary>
    public long CabinetStart { get; }

    /// <summary>Gets the folder records.</summary>
    public IReadOnlyList<CabFolder> Folders { get; }

    /// <summary>Gets the member file records in header order.</summary>
    public IReadOnlyList<CabFile> Files { get; }

    /// <summary>Gets a value indicating whether the header points at an adjacent cabinet.</summary>
    public bool IsMultiVolume =>
        !string.IsNullOrEmpty(PrevCabinet)
        || !string.IsNullOrEmpty(NextCabinet)
        || (Flags & 0x0003) != 0;

    /// <summary>Gets a value indicating whether the cabinet declares a reserved area in its data blocks.</summary>
    public bool HasDataReserve => (Flags & 0x0004) != 0;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="stream"/> starts with the cabinet signature "MSCF".
    /// </summary>
    public static bool HasSignature(Stream stream)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        var origin = stream.Position;
        try
        {
            var signature = new byte[4];
            return ReadExactly(stream, signature) == signature.Length && IsSignature(signature);
        }
        finally
        {
            stream.Position = origin;
        }
    }

    /// <summary>
    /// Reads and validates the header, folder records, file records, and optional reserved fields.
    /// Throws <see cref="CabDamagedException"/> for malformed or truncated input.
    /// </summary>
    public static CabArchive Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("Cabinet parsing requires a seekable stream.", nameof(stream));
        }

        stream.Position = 0;
        var prefix = new byte[HeaderPrefixBytes];
        if (ReadExactly(stream, prefix) != prefix.Length || !IsSignature(prefix))
        {
            throw new CabDamagedException();
        }

        var coffPrevCabinet = ReadUInt32(prefix, 4);
        var cbCabinet = ReadUInt32(prefix, 8);
        var coffNextCabinet = ReadUInt32(prefix, 12);
        var coffFiles = ReadUInt32(prefix, 16);
        var coffCabStart = ReadUInt32(prefix, 20);
        var versionMinor = prefix[24];
        var versionMajor = prefix[25];
        var folderCount = ReadUInt16(prefix, 26);
        var fileCount = ReadUInt16(prefix, 28);
        var flags = ReadUInt16(prefix, 30);

        if (cbCabinet < MinimumHeaderBytes
            || coffFiles < MinimumHeaderBytes
            || coffFiles >= cbCabinet
            || coffCabStart >= cbCabinet
            || versionMajor != 1
            || versionMinor > 3
            || (fileCount == 0 && folderCount == 0))
        {
            throw new CabDamagedException();
        }

        var fileRecordsEnd = 0L;
        var folders = ReadFolders(stream, folderCount, fileCount > 0);
        var files = ReadFiles(stream, fileCount, folders.Count, coffFiles, cbCabinet, versionMinor, flags,
            out fileRecordsEnd);

        // Optional strings follow the last file record; earlier offsets point into unrelated header data.
        var prevCabinet = ReadOptionalName(stream, coffPrevCabinet, fileRecordsEnd);
        var nextCabinet = ReadOptionalName(stream, coffNextCabinet, fileRecordsEnd);
        var folderReserveBytes = (flags & 0x0004) != 0 ? ReadByteOrZero(stream, coffCabStart, cbCabinet) : 0;

        return new CabArchive(prevCabinet, nextCabinet, flags, folderReserveBytes, coffCabStart, folders, files);
    }

    private static List<CabFolder> ReadFolders(Stream stream, int count, bool hasFiles)
    {
        var folders = new List<CabFolder>(count);
        for (var index = 0; index < count; index++)
        {
            var record = new byte[FolderRecordBytes];
            if (ReadExactly(stream, record) != record.Length)
            {
                throw new CabDamagedException();
            }

            folders.Add(new CabFolder(ReadUInt16(record, 0), ReadUInt16(record, 2)));
        }

        if (hasFiles && folders.TrueForAll(folder => folder.CompressionType is not (TypeStored or TypeMsZip
                or TypeQuantum or TypeLzx)))
        {
            // A compression type outside the defined set means the header cannot be trusted.
            throw new CabDamagedException();
        }

        return folders;
    }

    private static List<CabFile> ReadFiles(Stream stream, int count, int folderCount, long coffFiles, uint cbCabinet,
        int versionMinor, int flags, out long recordsEnd)
    {
        recordsEnd = coffFiles;
        var files = new List<CabFile>(count);
        var utf8Names = (flags & 0x0080) != 0;
        stream.Position = coffFiles;
        for (var index = 0; index < count; index++)
        {
            var headerOffset = stream.Position;
            var fixedPart = new byte[FileRecordFixedBytes];
            if (ReadExactly(stream, fixedPart) != fixedPart.Length)
            {
                throw new CabDamagedException();
            }

            var size = ReadUInt32(fixedPart, 0);
            var offset = ReadUInt32(fixedPart, 4);
            var folderIndex = ReadUInt16(fixedPart, 8);
            var date = ReadUInt16(fixedPart, 10);
            var time = ReadUInt16(fixedPart, 12);
            var dateValue = (ushort)date;
            var timeValue = (ushort)time;

            if (folderIndex >= folderCount)
            {
                throw new CabDamagedException();
            }

            // Cabinets older than version 1.3 store the file attributes in the first two bytes of szName;
            // those bytes may themselves contain NULs, so they are skipped before scanning for the terminator.
            var nameBytes = ReadNameBytes(stream, cbCabinet, versionMinor < 3 ? 2 : 0);

            files.Add(new CabFile(DecodeName(nameBytes, utf8Names), size, offset, headerOffset,
                (ushort)folderIndex, DecodeTimestamp(dateValue, timeValue) ?? DateTimeOffset.UnixEpoch));

            if (stream.Position > cbCabinet)
            {
                throw new CabDamagedException();
            }
        }

        recordsEnd = stream.Position;
        return files;
    }

    private static byte[] ReadNameBytes(Stream stream, uint cbCabinet, int attributeBytes)
    {
        for (var index = 0; index < attributeBytes; index++)
        {
            if (stream.ReadByte() < 0)
            {
                throw new CabDamagedException();
            }
        }

        var bytes = new List<byte>(64);
        while (true)
        {
            var value = stream.ReadByte();
            if (value < 0 || stream.Position > cbCabinet || bytes.Count >= MaxNameBytes)
            {
                throw new CabDamagedException();
            }

            if (value == 0)
            {
                return [.. bytes];
            }

            bytes.Add((byte)value);
        }
    }

    private static string DecodeName(byte[] bytes, bool utf8) =>
        (utf8 ? DecodeUtf8OrNull(bytes) : null) ?? Encoding.Latin1.GetString(bytes);

    private static string? DecodeUtf8OrNull(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string? ReadOptionalName(Stream stream, uint offset, long minimumOffset)
    {
        if (offset == 0 || offset < minimumOffset || offset >= stream.Length)
        {
            return null;
        }

        var origin = stream.Position;
        try
        {
            stream.Position = offset;
            var bytes = new List<byte>(32);
            while (bytes.Count < MaxOptionalFieldBytes && stream.Position < stream.Length)
            {
                var value = stream.ReadByte();
                if (value <= 0)
                {
                    break;
                }

                bytes.Add((byte)value);
            }

            return bytes.Count == 0 ? null : Encoding.Latin1.GetString([.. bytes]);
        }
        finally
        {
            stream.Position = origin;
        }
    }

    private static int ReadByteOrZero(Stream stream, uint offset, uint cbCabinet)
    {
        if (offset >= cbCabinet)
        {
            return 0;
        }

        var origin = stream.Position;
        try
        {
            stream.Position = offset;
            var value = stream.ReadByte();
            return value < 0 ? 0 : value;
        }
        finally
        {
            stream.Position = origin;
        }
    }

    private static DateTimeOffset? DecodeTimestamp(ushort date, ushort time)
    {
        var year = 1980 + ((date >> 9) & 0x7F);
        var month = (date >> 5) & 0x0F;
        var day = date & 0x1F;
        var hour = (time >> 11) & 0x1F;
        var minute = (time >> 5) & 0x3F;
        var second = (time & 0x1F) * 2;
        if (month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || minute > 59 || second > 59)
        {
            return null;
        }

        return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
    }

    private static bool IsSignature(ReadOnlySpan<byte> head) =>
        head.Length >= 4 && head[0] == (byte)'M' && head[1] == (byte)'S' && head[2] == (byte)'C'
        && head[3] == (byte)'F';

    private static int ReadExactly(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));

    private static int ReadUInt16(byte[] buffer, int offset) =>
        buffer[offset] | (buffer[offset + 1] << 8);
}
