using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SkyCD.Plugin.Metadata.Rar.Tests;

/// <summary>
/// Builds small RAR5 archives byte by byte so tests do not depend on opaque binary fixtures or external tools.
/// </summary>
/// <remarks>
/// Supported blocks are the main archive header, file headers with the stored (uncompressed) method, and the end
/// of archive header. Every block carries the BLAKE2sp header checksum the RAR5 format requires, so archives built
/// here go through exactly the same header parsing as archives produced by RAR itself.
/// </remarks>
internal static class RarTestArchiveBuilder
{
    private static readonly byte[] Signature = [(byte)'R', (byte)'a', (byte)'r', (byte)'!', 0x1A, 0x07, 0x01, 0x00];

    private const int MainBlockType = 1;
    private const int FileBlockType = 2;
    private const int EndBlockType = 5;

    private const int HeaderFlagsExtraArea = 0x0001;
    private const int HeaderFlagsDataArea = 0x0002;
    private const int ExtraRecordCrypt = 1;

    // RAR5 header flags shared by every block: the split-before/split-after flags.
    private const int BlockFlagsSplitBefore = 0x0008;
    private const int BlockFlagsSplitAfter = 0x0010;

    private const int FileFlagsDirectory = 0x0001;
    private const int FileFlagsUnixTime = 0x0002;
    private const int FileFlagsCrc32 = 0x0004;
    private const int FileAttributeArchive = 0x20;

    public static MemoryStream CreateStored(params (string Path, string Content)[] entries) =>
        Create(encrypted: false, mainFlags: 0, endFlags: 0, serviceHeaders: false, entries);

    public static MemoryStream CreateEncrypted(params (string Path, string Content)[] entries) =>
        Create(encrypted: true, mainFlags: 0, endFlags: 0, serviceHeaders: false, entries);

    /// <summary>Builds an archive whose main header marks it as a volume and whose end header announces a
    /// following volume.</summary>
    public static MemoryStream CreateMultiVolume(params (string Path, string Content)[] entries) =>
        Create(encrypted: false, mainFlags: 0x0001, endFlags: 0x0001, serviceHeaders: false, entries);

    /// <summary>
    /// Builds an archive that also records the service headers RAR writes for a single file file header: the
    /// entry is stored as data plus a zero length continuation header (the Quick Open offset cache) that carries
    /// the trailing path separator and no data.
    /// </summary>
    public static MemoryStream CreateWithServiceHeaders(params (string Path, string Content)[] entries) =>
        Create(encrypted: false, mainFlags: 0, endFlags: 0, serviceHeaders: true, entries);

    public static MemoryStream CreateDamaged() =>
        new(Encoding.ASCII.GetBytes("not a rar archive"));

    /// <summary>
    /// Builds an archive whose stored file header announces the split-after flag, so the entry continues in a
    /// volume that is not present. Readers that ignore the flag would report a truncated entry as complete.
    /// </summary>
    public static MemoryStream CreateSplitEntry(string path, string content)
    {
        var stream = new MemoryStream();
        stream.Write(Signature);
        WriteBlock(stream, MainBlockType, 0, [0]);
        var payload = Encoding.UTF8.GetBytes(content);
        WriteFileHeader(stream, path, payload.Length, encrypted: false, isDirectory: false,
            Crc32.Compute(payload), extraFlags: BlockFlagsSplitBefore | BlockFlagsSplitAfter);
        stream.Write(payload);
        WriteBlock(stream, EndBlockType, 0, [0]);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream Create(bool encrypted, int mainFlags, int endFlags, bool serviceHeaders,
        (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        stream.Write(Signature);
        WriteBlock(stream, MainBlockType, 0, [(byte)mainFlags]);

        foreach (var (path, content) in entries)
        {
            var payload = Encoding.UTF8.GetBytes(content);
            var isDirectory = path.EndsWith('/');
            WriteFileHeader(stream, path, payload.Length, encrypted, isDirectory, Crc32.Compute(payload),
                dataSize: isDirectory ? 0 : payload.Length);
            if (!isDirectory)
            {
                stream.Write(payload);
            }

            if (serviceHeaders && !isDirectory)
            {
                // RAR writes a zero length "service" header for the Quick Open cache right after the data.
                WriteFileHeader(stream, path + "/", 0, encrypted, isDirectory: true, crc: 0, dataSize: 0);
            }
        }

        WriteBlock(stream, EndBlockType, 0, [(byte)endFlags]);

        stream.Position = 0;
        return stream;
    }

    private static void WriteFileHeader(Stream stream, string path, int size, bool encrypted, bool isDirectory,
        uint crc, int extraFlags = 0, int dataSize = 0)
    {
        var extraArea = encrypted ? BuildCryptoExtraArea() : null;
        var fileBody = new List<byte>();
        WriteVInt(fileBody, FileFlagsUnixTime | FileFlagsCrc32 | (isDirectory ? FileFlagsDirectory : 0));
        WriteVInt(fileBody, size);
        WriteVInt(fileBody, FileAttributeArchive);
        WriteUInt32(fileBody, (uint)new DateTimeOffset(2024, 1, 2, 3, 4, 6, TimeSpan.Zero).ToUnixTimeSeconds());
        WriteUInt32(fileBody, crc);
        WriteVInt(fileBody, 0); // compression information: version 0, stored method
        WriteVInt(fileBody, 0); // host OS: Windows
        // RAR5 stores entry names with forward slashes; the archive reader maps them to the host separator.
        var name = Encoding.UTF8.GetBytes(path);
        WriteVInt(fileBody, name.Length);
        fileBody.AddRange(name);

        var blockFlags = (dataSize > 0 ? HeaderFlagsDataArea : 0)
                         | (extraArea is null ? 0 : HeaderFlagsExtraArea)
                         | extraFlags;
        WriteBlock(stream, FileBlockType, blockFlags, fileBody, dataSize, extraArea);
    }

    /// <summary>
    /// Writes one RAR5 block: CRC32 of the header, the header size (including the size field), the type and
    /// flags, the type specific body, then the data area size when the block carries data.
    /// </summary>
    private static void WriteBlock(Stream stream, int blockType, int blockFlags, List<byte> body, int dataSize = 0,
        List<byte>? extraArea = null)
    {
        var header = new List<byte>();
        WriteVInt(header, blockType);
        WriteVInt(header, blockFlags);
        if ((blockFlags & HeaderFlagsExtraArea) != 0)
        {
            WriteVInt(header, extraArea?.Count ?? 0);
        }

        if ((blockFlags & HeaderFlagsDataArea) != 0)
        {
            WriteVInt(header, dataSize);
        }

        header.AddRange(body);
        if (extraArea is not null)
        {
            header.AddRange(extraArea);
        }

        // The size field counts the header bytes after itself: type, flags, optional data size, and body.
        if (header.Count >= 0x80)
        {
            throw new InvalidOperationException("Fixture header blocks must stay below 128 bytes.");
        }

        var serialized = new List<byte> { (byte)header.Count };
        serialized.AddRange(header);

        // Header blocks, like RAR5 file data, carry a standard CRC-32 of every byte after the checksum.
        var checksum = Crc32.Compute(serialized.ToArray());
        stream.WriteByte((byte)checksum);
        stream.WriteByte((byte)(checksum >> 8));
        stream.WriteByte((byte)(checksum >> 16));
        stream.WriteByte((byte)(checksum >> 24));
        stream.Write(serialized.ToArray());
    }

    /// <summary>
    /// Builds the RAR5 extra area record that marks an entry as encrypted: version 0, a password check flag,
    /// the KDF work factor, salt, init vector, and the password check with its SHA-256 verification prefix.
    /// </summary>
    private static List<byte> BuildCryptoExtraArea()
    {
        var body = new List<byte>();
        WriteVInt(body, 0); // crypto version
        WriteVInt(body, 1); // flags: password check present
        body.Add(15); // KDF log2 count
        for (var index = 1; index <= 16; index++)
        {
            body.Add((byte)index); // salt
        }

        for (var index = 0; index < 16; index++)
        {
            body.Add((byte)(0xA0 + index)); // init vector
        }

        var passwordCheck = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        body.AddRange(passwordCheck);
        body.AddRange(SHA256.HashData(passwordCheck).AsSpan(0, 4).ToArray());

        var payload = new List<byte> { ExtraRecordCrypt };
        payload.AddRange(body);
        var record = new List<byte>();
        WriteVInt(record, payload.Count);
        record.AddRange(payload);
        return record;
    }

    private static void WriteUInt32(List<byte> target, uint value)
    {
        target.Add((byte)value);
        target.Add((byte)(value >> 8));
        target.Add((byte)(value >> 16));
        target.Add((byte)(value >> 24));
    }

    private static void WriteVInt(List<byte> target, long value)
    {
        while (value >= 0x80)
        {
            target.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        target.Add((byte)value);
    }
}

/// <summary>CRC-32 as used by ZIP and the RAR5 file checksum.</summary>
internal static class Crc32
{
    private static readonly uint[] Table = BuildTable();

    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = (crc >> 8) ^ Table[(crc ^ value) & 0xFF];
        }

        return ~crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (var index = 0; index < table.Length; index++)
        {
            var entry = (uint)index;
            for (var bit = 0; bit < 8; bit++)
            {
                entry = (entry & 1) == 1 ? (entry >> 1) ^ 0xEDB88320u : entry >> 1;
            }

            table[index] = entry;
        }

        return table;
    }
}
