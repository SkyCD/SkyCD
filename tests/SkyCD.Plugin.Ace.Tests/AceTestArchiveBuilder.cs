using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.Ace.Tests;

/// <summary>
/// Builds minimal ACE 2.0 archives byte by byte so tests do not depend on binary fixtures or external
/// tools. Each block is a 16-bit header CRC over the header starting at the header type, the header size,
/// the header type and flags, the type-specific body and, for stored entries, the entry data. The main
/// header carries the <c>**ACE**</c> signature at offset 7 of the file, as real ACE archives do.
/// </summary>
internal static class AceTestArchiveBuilder
{
    /// <summary>Default modification time stored in every fixture header.</summary>
    public static readonly DateTime ModifiedUtc = new(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc);

    private const byte TypeMain = 0;
    private const byte TypeFile32 = 1;
    private const int SignatureLength = 7;
    private const int TypeAndFlagsLength = 3;

    private const ushort FlagAddSize = 1 << 0;
    private const ushort FlagV20Format = 1 << 8;
    private const ushort FlagMultiVolume = 1 << 11;
    private const ushort FlagContinuedNext = 1 << 13;
    private const ushort FlagPassword = 1 << 14;
    private const ushort FlagRecovery = 1 << 13;

    private const uint AttributeDirectory = 0x10;
    private const byte CompressionStored = 0;
    private const byte CompressionBlocked = 2;

    /// <summary>Creates an archive whose entries use the stored method and carry their plain-text data.</summary>
    public static MemoryStream CreateStored(params (string Path, string Content)[] entries) =>
        Create(entries, fileFlags: 0, mainFlags: FlagV20Format, CompressionStored, writeData: true);

    /// <summary>Creates an archive whose entries use a compressed method and carry no data after the header.</summary>
    public static MemoryStream CreateCompressed(byte method = 4, params (string Path, string Content)[] entries) =>
        Create(entries, fileFlags: 0, mainFlags: FlagV20Format, CompressionBlocked, writeData: false, method);

    /// <summary>Creates an archive with one stored entry followed by one compressed entry.</summary>
    public static MemoryStream CreateStoredAndCompressed((string Path, string Content) stored,
        (string Path, string Content) compressed)
    {
        var stream = new MemoryStream();
        WriteBlock(stream, TypeMain, FlagV20Format, BuildMainBody());
        WriteEntry(stream, stored, fileFlags: 0, CompressionStored, writeData: true);
        WriteEntry(stream, compressed, fileFlags: 0, CompressionBlocked, writeData: false, compressionQuality: 4);
        stream.Position = 0;
        return stream;
    }

    /// <summary>Creates an archive whose entries are marked encrypted.</summary>
    public static MemoryStream CreateEncrypted(params (string Path, string Content)[] entries) =>
        Create(entries, FlagPassword, FlagV20Format, CompressionStored, writeData: false);

    /// <summary>Creates an archive whose entries are marked as parts of a multi-volume set.</summary>
    public static MemoryStream CreateMultiVolume(params (string Path, string Content)[] entries) =>
        Create(entries, FlagContinuedNext, FlagV20Format | FlagMultiVolume, CompressionStored, writeData: false);

    /// <summary>Creates an archive whose main header declares a recovery record.</summary>
    public static MemoryStream CreateWithRecoveryRecord(params (string Path, string Content)[] entries) =>
        Create(entries, fileFlags: 0, mainFlags: FlagV20Format | FlagRecovery, CompressionStored, writeData: true);

    /// <summary>Creates the byte image of a minimal catalog used by committed fixtures.</summary>
    public static byte[] CreateCatalogFixture()
    {
        using var archive = CreateStored(
            ("catalog/readme.txt", "hello ace"),
            ("catalog/data/track.bin", "track-data-0123456789"));
        return archive.ToArray();
    }

    public static MemoryStream CreateDamaged() =>
        new(Encoding.ASCII.GetBytes("not an ace archive"));

    /// <summary>Creates a valid archive, then corrupts the CRC-32 of its first entry header.</summary>
    public static MemoryStream CreateStoredWithBadEntryCrc((string Path, string Content) entry)
    {
        var bytes = CreateStored(entry).ToArray();
        var headerOffset = EntryHeaderOffset(bytes);
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(headerOffset + 2));
        var crcOffset = headerOffset + 4 + TypeAndFlagsLength + 4 + 4 + 4 + 4;
        bytes[crcOffset] ^= 0x01;
        RewriteHeaderChecksum(bytes.AsSpan(headerOffset, 4 + headerLength));
        return new MemoryStream(bytes);
    }

    /// <summary>Creates a valid archive, then corrupts the CRC-16 head checksum of its first entry header.</summary>
    public static MemoryStream CreateStoredWithBadHeaderChecksum((string Path, string Content) entry)
    {
        var bytes = CreateStored(entry).ToArray();
        var headerOffset = EntryHeaderOffset(bytes);
        bytes[headerOffset] ^= 0x01;
        return new MemoryStream(bytes);
    }

    /// <summary>Creates a valid archive and truncates it in the middle of the entry data.</summary>
    public static MemoryStream CreateTruncated((string Path, string Content) entry)
    {
        var bytes = CreateStored(entry).ToArray();
        return new MemoryStream(bytes[..(bytes.Length - 3)]);
    }

    /// <summary>The offset of the first file block: the complete main block and its prefix.</summary>
    public static int EntryHeaderOffset(ReadOnlySpan<byte> archive)
    {
        var mainLength = BinaryPrimitives.ReadUInt16LittleEndian(archive[2..]);
        return 4 + mainLength;
    }

    /// <summary>Returns the complete byte image of an archive created by this builder.</summary>
    public static byte[] ToBytes(MemoryStream archive) => archive.ToArray();

    /// <summary>The ACE CRC-32: the standard reflected CRC-32 register without the final inversion.</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    /// <summary>The ACE CRC-16: the lower 16 bits of the ACE CRC-32.</summary>
    public static ushort Crc16(ReadOnlySpan<byte> data) => (ushort)Crc32(data);

    private static readonly uint[] Table = BuildTable();

    private static MemoryStream Create((string Path, string Content)[] entries, ushort fileFlags, ushort mainFlags,
        byte compressionType, bool writeData, byte compressionQuality = 0)
    {
        var stream = new MemoryStream();
        WriteBlock(stream, TypeMain, mainFlags, BuildMainBody());
        foreach (var entry in entries)
        {
            WriteEntry(stream, entry, fileFlags, compressionType, writeData, compressionQuality);
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(Stream stream, (string Path, string Content) entry, ushort fileFlags,
        byte compressionType, bool writeData, byte compressionQuality = 0)
    {
        var isDirectory = entry.Path.EndsWith('/');
        var name = Encoding.UTF8.GetBytes(isDirectory ? entry.Path.TrimEnd('/') : entry.Path);
        var data = isDirectory ? [] : Encoding.UTF8.GetBytes(entry.Content);
        var flags = (ushort)(fileFlags | FlagAddSize);
        var body = BuildFileBody(name, data, flags, compressionType, compressionQuality, isDirectory, writeData);
        WriteBlock(stream, TypeFile32, flags, body);
        if (writeData && !isDirectory)
        {
            stream.Write(data);
        }
    }

    private static byte[] BuildMainBody()
    {
        var body = new List<byte>();
        body.AddRange("**ACE**"u8); // signature at file offset 7
        body.Add(2); // extraction version
        body.Add(2); // creation version
        body.Add(2); // host (Win32)
        body.Add(0); // volume number
        body.AddRange(UInt32Bytes(DosTimestamp(ModifiedUtc)));
        body.AddRange(new byte[8]); // reserved
        body.Add(0); // advert string length
        return body.ToArray();
    }

    private static byte[] BuildFileBody(byte[] name, byte[] data, ushort flags, byte compressionType,
        byte compressionQuality, bool isDirectory, bool writeData)
    {
        var packedSize = writeData && !isDirectory ? data.Length : 0;
        var body = new List<byte>();
        body.AddRange(UInt32Bytes((uint)packedSize));
        body.AddRange(UInt32Bytes((uint)data.Length));
        body.AddRange(UInt32Bytes(DosTimestamp(ModifiedUtc)));
        body.AddRange(UInt32Bytes(isDirectory ? AttributeDirectory : 0x20)); // archive attribute
        body.AddRange(UInt32Bytes(isDirectory ? 0 : Crc32(data)));
        body.Add(compressionType);
        body.Add(compressionQuality);
        body.AddRange(UInt16Bytes(0)); // decompression parameters
        body.AddRange(UInt16Bytes(0)); // reserved
        body.AddRange(UInt16Bytes((ushort)name.Length));
        body.AddRange(name);
        return body.ToArray();
    }

    private static void WriteBlock(Stream stream, byte type, ushort flags, byte[] body)
    {
        var chunk = new byte[TypeAndFlagsLength + body.Length];
        chunk[0] = type;
        chunk[1] = (byte)flags;
        chunk[2] = (byte)(flags >> 8);
        body.CopyTo(chunk, TypeAndFlagsLength);

        var block = new byte[4 + chunk.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(block, Crc16(chunk));
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), (ushort)chunk.Length);
        chunk.CopyTo(block, 4);
        stream.Write(block);
    }

    private static void RewriteHeaderChecksum(Span<byte> block)
    {
        var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        BinaryPrimitives.WriteUInt16LittleEndian(block, Crc16(block.Slice(4, headerLength)));
    }

    private static uint DosTimestamp(DateTime value)
    {
        var date = (value.Year - 1980) << 9 | value.Month << 5 | value.Day;
        var time = value.Hour << 11 | value.Minute << 5 | value.Second / 2;
        return (uint)(date << 16 | time);
    }

    private static byte[] UInt16Bytes(ushort value) => [(byte)value, (byte)(value >> 8)];

    private static byte[] UInt32Bytes(uint value) =>
        [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var value = (uint)index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ 0xEDB88320u : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
