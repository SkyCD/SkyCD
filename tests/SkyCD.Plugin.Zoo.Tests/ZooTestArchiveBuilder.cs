using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.Zoo.Parsing;

namespace SkyCD.Plugin.Zoo.Tests;

/// <summary>
/// Builds minimal ZOO 2.10 archives (stored, LZD, and deliberately unsupported members) so tests do not
/// depend on binary fixtures or external tools. The layout follows the public-domain zoo 2.10 sources.
/// </summary>
internal static class ZooTestArchiveBuilder
{
    private const uint ArchiveTag = 0xFDC4A7DC;
    private const int HeaderBytes = 42;
    private const int LongEntryBytes = 56;

    public static MemoryStream CreateStored(params (string Path, string Content)[] entries) =>
        Create(0, corruptCrc: false, ToBytes(entries));

    public static MemoryStream CreateStored(params (string Path, byte[] Content)[] entries) =>
        Create(0, corruptCrc: false, entries);

    public static MemoryStream CreateLzd(params (string Path, string Content)[] entries) =>
        Create(1, corruptCrc: false, ToBytes(entries));

    public static MemoryStream CreateLzd(params (string Path, byte[] Content)[] entries) =>
        Create(1, corruptCrc: false, entries);

    public static MemoryStream CreateUnsupported(params (string Path, string Content)[] entries) =>
        Create(2, corruptCrc: false, ToBytes(entries));

    public static MemoryStream CreateWithCorruptCrc(params (string Path, string Content)[] entries) =>
        Create(0, corruptCrc: true, ToBytes(entries));

    public static MemoryStream CreateDamaged() =>
        new(Encoding.ASCII.GetBytes("not a zoo archive"));

    private static (string Path, byte[] Content)[] ToBytes((string Path, string Content)[] entries)
    {
        var result = new (string, byte[])[entries.Length];
        for (var index = 0; index < entries.Length; index++)
        {
            result[index] = (entries[index].Path, Encoding.UTF8.GetBytes(entries[index].Content));
        }

        return result;
    }

    private static MemoryStream Create(int packingMethod, bool corruptCrc,
        (string Path, byte[] Content)[] entries)
    {
        var stream = new MemoryStream();
        WriteHeader(stream);
        for (var index = 0; index < entries.Length; index++)
        {
            var (path, content) = entries[index];
            WriteFileEntry(stream, path, content, packingMethod, corruptCrc && index == 0);
        }

        WriteTerminator(stream);
        stream.Position = 0;
        return stream;
    }

    private static void WriteHeader(Stream stream)
    {
        var header = new byte[HeaderBytes];
        Encoding.ASCII.GetBytes("ZOO 2.10 Archive.\x1A").CopyTo(header, 0);
        WriteUInt32(header, 20, ArchiveTag);
        WriteInt32(header, 24, HeaderBytes);
        WriteInt32(header, 28, -HeaderBytes);
        header[32] = 2;
        header[33] = 0;
        header[34] = 1;
        stream.Write(header);
    }

    private static void WriteFileEntry(Stream stream, string path, byte[] content, int packingMethod,
        bool corruptCrc)
    {
        var payload = packingMethod == 1 ? CompressLzd(content) : content;
        var separator = path.LastIndexOf('/');
        var directory = separator > 0 ? path[..separator] : string.Empty;
        var name = separator > 0 ? path[(separator + 1)..] : path;
        var nameBytes = Encoding.Latin1.GetBytes(name);
        var directoryBytes = Encoding.Latin1.GetBytes(directory);
        var variableLength = 10 + nameBytes.Length + directoryBytes.Length;
        var entrySize = LongEntryBytes + variableLength;
        var dataOffset = stream.Position + entrySize + 5;
        var nextOffset = dataOffset + payload.Length;
        var modified = new DateTime(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc);

        Span<byte> fixedBytes = stackalloc byte[LongEntryBytes];
        fixedBytes.Clear();
        WriteUInt32(fixedBytes[..4], 0, ArchiveTag);
        fixedBytes[4] = 2;
        fixedBytes[5] = (byte)packingMethod;
        WriteInt32(fixedBytes[6..10], 0, (int)nextOffset);
        WriteInt32(fixedBytes[10..14], 0, (int)dataOffset);
        var dosDate = (ushort)(((modified.Year - 1980) << 9) | (modified.Month << 5) | modified.Day);
        var dosTime = (ushort)((modified.Hour << 11) | (modified.Minute << 5) | (modified.Second / 2));
        WriteUInt16(fixedBytes[14..16], 0, dosDate);
        WriteUInt16(fixedBytes[16..18], 0, dosTime);
        var crc = (ushort)(ZooCrc16.Compute(content) + (corruptCrc ? 1 : 0));
        WriteUInt16(fixedBytes[18..20], 0, crc);
        WriteInt32(fixedBytes[20..24], 0, content.Length);
        WriteInt32(fixedBytes[24..28], 0, payload.Length);
        fixedBytes[28] = 2;
        fixedBytes[29] = 0;
        var shortName = Encoding.Latin1.GetBytes(name.Length > 12 ? name[..12] : name);
        shortName.CopyTo(fixedBytes[38..51]);
        WriteUInt16(fixedBytes[51..53], 0, (ushort)variableLength);
        fixedBytes[53] = 127;
        WriteUInt16(fixedBytes[54..56], 0, 0);
        stream.Write(fixedBytes);

        var variable = new byte[variableLength];
        variable[0] = (byte)nameBytes.Length;
        variable[1] = (byte)directoryBytes.Length;
        nameBytes.CopyTo(variable, 2);
        directoryBytes.CopyTo(variable, 2 + nameBytes.Length);
        stream.Write(variable);

        stream.Write([(byte)'@', (byte)')', (byte)'#', (byte)'(', 0]);
        stream.Write(payload);
    }

    private static void WriteTerminator(Stream stream)
    {
        var terminator = new byte[LongEntryBytes];
        WriteUInt32(terminator, 0, ArchiveTag);
        terminator[4] = 2;
        stream.Write(terminator);
    }

    /// <summary>Standard LZW encoder matching the ZOO LZD decoder (9-13 bit codes, LSB-first).</summary>
    public static byte[] CompressLzd(ReadOnlySpan<byte> input)
    {
        var writer = new BitWriter();
        var table = new Dictionary<int, int>();
        var bitCount = 9;
        var maxCode = 512;
        var freeCode = 258;

        writer.Write(256, bitCount);
        if (input.Length == 0)
        {
            writer.Write(257, bitCount);
            return writer.Finish();
        }

        int current = input[0];
        for (var index = 1; index < input.Length; index++)
        {
            int next = input[index];
            var key = (current << 8) | next;
            if (table.TryGetValue(key, out var code))
            {
                current = code;
                continue;
            }

            if (freeCode < 8192)
            {
                table[key] = freeCode++;
            }

            writer.Write(current, bitCount);
            if (freeCode > maxCode)
            {
                if (bitCount >= 13)
                {
                    writer.Write(256, bitCount);
                    table.Clear();
                    bitCount = 9;
                    maxCode = 512;
                    freeCode = 258;
                }
                else
                {
                    bitCount++;
                    maxCode <<= 1;
                }
            }

            current = next;
        }

        writer.Write(current, bitCount);
        writer.Write(257, bitCount);
        return writer.Finish();
    }

    private static void WriteUInt16(Span<byte> target, int offset, ushort value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(Span<byte> target, int offset, uint value)
    {
        target[offset] = (byte)value;
        target[offset + 1] = (byte)(value >> 8);
        target[offset + 2] = (byte)(value >> 16);
        target[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteInt32(Span<byte> target, int offset, int value) =>
        WriteUInt32(target, offset, unchecked((uint)value));

    private sealed class BitWriter
    {
        private readonly MemoryStream stream = new();
        private ulong buffer;
        private int bits;

        public void Write(int code, int count)
        {
            buffer |= (ulong)(uint)(code & 0xFFFF) << bits;
            bits += count;
            while (bits >= 8)
            {
                stream.WriteByte((byte)(buffer & 0xFF));
                buffer >>= 8;
                bits -= 8;
            }
        }

        public byte[] Finish()
        {
            if (bits > 0)
            {
                stream.WriteByte((byte)(buffer & 0xFF));
            }

            return stream.ToArray();
        }
    }
}
