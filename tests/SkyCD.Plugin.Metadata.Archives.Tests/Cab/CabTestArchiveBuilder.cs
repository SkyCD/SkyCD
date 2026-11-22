using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace SkyCD.Plugin.Metadata.Archives.Cab.Tests;

/// <summary>
/// Writes Microsoft Cabinet files byte by byte so tests do not depend on external tooling.
/// </summary>
internal static class CabTestArchiveBuilder
{
    /// <summary>A member of a test cabinet.</summary>
    internal sealed record Member(string Name, byte[] Content);

    /// <summary>Options controlling the exact bytes a test cabinet contains.</summary>
    internal sealed class Options
    {
        public string? PrevCabinet { get; init; }
        public string? NextCabinet { get; init; }
        public int FolderCount { get; init; } = 1;
        public int CompressionTypeOverride { get; init; } = -1;
        public int? DataBlockSize { get; init; }
        public bool IncludeChecksum { get; init; }
        public bool OmitCkSignature { get; init; }
        public bool PrefixedNames { get; init; }
        public int PrefixLength { get; init; }
        public int VersionMinor { get; init; } = 3;
    }

    /// <summary>Raw folder bytes plus the sorted member list of the folder.</summary>
    private sealed record FolderPlan(byte[] Data, List<Member> Sorted);

    public static MemoryStream CreateStored(IReadOnlyList<Member> members, Options options) =>
        Build(0, [new List<Member>(members)], options);

    public static MemoryStream CreateMsZip(IReadOnlyList<Member> members, Options options) =>
        Build(1, [new List<Member>(members)], options);

    /// <summary>Creates a cabinet with the requested compression type and member layout.</summary>
    public static MemoryStream Create(int compressionType, IReadOnlyList<Member> members, Options? options = null) =>
        Build(compressionType, [new List<Member>(members)], options ?? new Options());

    /// <summary>Creates a cabinet whose members are spread over <see cref="Options.FolderCount"/> folders.</summary>
    public static MemoryStream CreateSplit(IReadOnlyList<Member> members, Options options)
    {
        var folderCount = Math.Clamp(options.FolderCount, 1, members.Count);
        if (folderCount == 1)
        {
            return Build(0, [new List<Member>(members)], options);
        }

        var plans = new List<List<Member>>(folderCount) { new() { members[0] } };
        var remaining = members.Count - 1;
        var foldersLeft = folderCount - 1;
        var cursor = 1;
        for (var index = 0; index < foldersLeft; index++)
        {
            var take = remaining / (foldersLeft - index);
            plans.Add(members.Skip(cursor).Take(take).ToList());
            cursor += take;
            remaining -= take;
        }

        return Build(0, plans, options);
    }

    public static MemoryStream CreateMsZip(params (string Name, string Content)[] entries)
    {
        var members = new List<Member>(entries.Length);
        foreach (var (name, content) in entries)
        {
            members.Add(new Member(name, Encoding.UTF8.GetBytes(content)));
        }

        return CreateMsZip(members, new Options { IncludeChecksum = true });
    }

    public static MemoryStream CreateStored(params (string Name, string Content)[] entries)
    {
        var members = new List<Member>(entries.Length);
        foreach (var (name, content) in entries)
        {
            members.Add(new Member(name, Encoding.UTF8.GetBytes(content)));
        }

        return CreateStored(members, new Options());
    }

    /// <summary>Creates a stored cabinet whose file records carry an attribute prefix before each name.</summary>
    public static MemoryStream CreateStoredWithPrefixedNames(IReadOnlyList<Member> members) =>
        CreateStored(members, new Options { PrefixedNames = true, PrefixLength = 2, VersionMinor = 1 });


    private static MemoryStream Build(int compressionType, List<List<Member>> folderMembers, Options options)
    {
        var plans = new List<FolderPlan>(folderMembers.Count);
        foreach (var folder in folderMembers)
        {
            var sorted = new List<Member>(folder);
            sorted.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
            var data = new MemoryStream();
            var blockSize = options.DataBlockSize ?? 0;
            if (compressionType == 0)
            {
                var raw = sorted.SelectMany(member => member.Content).ToArray();
                var chunks = blockSize <= 0 ? [raw] : Cut(raw, blockSize);
                WriteBlocks(data, chunks, stored: true, checksum: options.IncludeChecksum,
                    omitSignature: options.OmitCkSignature);
            }
            else if (compressionType == 1)
            {
                var raw = sorted.SelectMany(member => member.Content).ToArray();
                WriteBlocks(data, Cut(raw, 32_768), stored: false, checksum: options.IncludeChecksum,
                    omitSignature: options.OmitCkSignature);
            }
            else
            {
                // Unknown / unsupported compression: emit opaque payload bytes the reader never decodes.
                var opaque = new byte[sorted.Sum(member => member.Content.Length) + 16];
                new Random(42).NextBytes(opaque);
                WriteFrame(data, opaque, opaque.Length, options.IncludeChecksum);
            }

            plans.Add(new FolderPlan(data.ToArray(), sorted));
        }

        var fileCount = folderMembers.Sum(folder => folder.Count);
        var folderCount = plans.Count;
        var coffFiles = 36 + folderCount * 8;
        var nameBytes = new List<(string Name, byte[] Bytes)>(fileCount);
        foreach (var plan in plans)
        {
            foreach (var member in plan.Sorted)
            {
                nameBytes.Add((member.Name, EncodeName(member.Name, options.PrefixedNames, options.PrefixLength)));
            }
        }

        var fileRecordsSize = nameBytes.Sum(entry => 16 + entry.Bytes.Length + 1);
        var coffCabStart = coffFiles + fileRecordsSize;

        var payload = new MemoryStream();
        var folderOffsets = new List<long>(folderCount);
        foreach (var plan in plans)
        {
            folderOffsets.Add(payload.Length);
            payload.Write(plan.Data, 0, plan.Data.Length);
        }

        var payloadBytes = payload.ToArray();
        var cbCabinet = coffCabStart + payloadBytes.Length;
        var prefixBytes = new byte[36];
        WriteAscii(prefixBytes, 0, "MSCF");
        WriteUInt32(prefixBytes, 8, (uint)cbCabinet);
        WriteUInt32(prefixBytes, 16, (uint)coffFiles);
        WriteUInt32(prefixBytes, 20, (uint)coffCabStart);
        prefixBytes[24] = (byte)options.VersionMinor;
        prefixBytes[25] = 1;
        WriteUInt16(prefixBytes, 26, (ushort)folderCount);
        WriteUInt16(prefixBytes, 28, (ushort)fileCount);
        WriteUInt16(prefixBytes, 30, 0);
        WriteUInt16(prefixBytes, 32, 0x1234);
        WriteUInt16(prefixBytes, 34, 0);

        var trailing = new MemoryStream();
        if (options.PrevCabinet is not null)
        {
            WriteUInt32(prefixBytes, 4, (uint)(cbCabinet + trailing.Length));
            trailing.Write(Encoding.Latin1.GetBytes(options.PrevCabinet));
            trailing.WriteByte(0);
        }

        if (options.NextCabinet is not null)
        {
            WriteUInt32(prefixBytes, 12, (uint)(cbCabinet + trailing.Length));
            trailing.Write(Encoding.Latin1.GetBytes(options.NextCabinet));
            trailing.WriteByte(0);
        }

        var cabinet = new MemoryStream();
        cabinet.Write(prefixBytes);
        foreach (var plan in plans)
        {
            var record = new byte[8];
            WriteUInt16(record, 0, (ushort)(options.CompressionTypeOverride >= 0
                ? options.CompressionTypeOverride
                : compressionType));
            WriteUInt16(record, 2, (ushort)CountFrames(plan.Data));
            cabinet.Write(record);
        }

        var nameIndex = 0;
        for (var folderIndex = 0; folderIndex < plans.Count; folderIndex++)
        {
            var plan = plans[folderIndex];
            long offset = 0;
            foreach (var member in plan.Sorted)
            {
                var record = new byte[16];
                WriteUInt32(record, 0, (uint)member.Content.Length);
                WriteUInt32(record, 4, (uint)offset);
                WriteUInt16(record, 8, (ushort)folderIndex);
                WriteUInt16(record, 10, (ushort)((46 << 9) | (3 << 5) | 21));
                WriteUInt16(record, 12, (ushort)((12 << 11) | (34 << 5) | 5));
                cabinet.Write(record);
                cabinet.Write(nameBytes[nameIndex].Bytes);
                cabinet.WriteByte(0);
                nameIndex++;
                offset += member.Content.Length;
            }
        }

        cabinet.Write(payloadBytes);
        cabinet.Write(trailing.ToArray());
        cabinet.Position = 0;
        return cabinet;
    }

    private static int CountFrames(byte[] data)
    {
        var position = 0;
        var count = 0;
        while (position + 8 <= data.Length && count < 65_535)
        {
            var cbData = data[position + 4] | (data[position + 5] << 8);
            if (cbData < 8)
            {
                break;
            }

            position += cbData;
            count++;
        }

        return count;
    }

    private static List<byte[]> Cut(byte[] data, int blockSize)
    {
        var chunks = new List<byte[]>();
        for (var start = 0; start < data.Length; start += blockSize)
        {
            chunks.Add(data[start..Math.Min(start + blockSize, data.Length)]);
        }

        return chunks.Count == 0 ? [[]] : chunks;
    }

    private static void WriteBlocks(MemoryStream target, List<byte[]> chunks, bool stored,
        bool checksum, bool omitSignature)
    {
        foreach (var raw in chunks)
        {
            if (stored)
            {
                WriteFrame(target, raw, raw.Length, checksum);
                continue;
            }

            var compressed = new MemoryStream();
            using (var deflate = new DeflateStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(raw);
            }

            var framed = omitSignature ? compressed.ToArray() : [.. "CK"u8, .. compressed.ToArray()];
            WriteFrame(target, framed, raw.Length, checksum);
        }
    }

    private static void WriteFrame(MemoryStream target, byte[] data, int uncompressedSize, bool checksum)
    {
        var cbData = 8 + data.Length;
        if (cbData > 0xFFFF)
        {
            throw new InvalidOperationException("Test block exceeds the CFDATA limit.");
        }

        var header = new byte[8];
        if (checksum)
        {
            WriteUInt32(header, 0, Checksum(data));
        }

        WriteUInt16(header, 4, (ushort)cbData);
        WriteUInt16(header, 6, (ushort)Math.Min(uncompressedSize, 0xFFFF));
        target.Write(header);
        target.Write(data);
    }

    /// <summary>Computes the CFDATA checksum used by cabinets that store one.</summary>
    public static uint Checksum(byte[] data)
    {
        uint csum = 0;
        for (var index = 0; index < data.Length; index += 4)
        {
            var value = (uint)(data[index]
                               | (index + 1 < data.Length ? data[index + 1] << 8 : 0)
                               | (index + 2 < data.Length ? data[index + 2] << 16 : 0)
                               | (index + 3 < data.Length ? data[index + 3] << 24 : 0));
            csum ^= value;
        }

        return csum;
    }

    private static byte[] EncodeName(string name, bool prefixed, int prefixLength)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        if (!prefixed)
        {
            return bytes;
        }

        // Cabinets older than version 1.3 store two little-endian attribute bytes (0x21 = HIDDEN, 0x20 =
        // ARCHIVE) in front of the file name.
        var prefix = new byte[Math.Max(prefixLength, 2)];
        prefix[0] = 0x21;
        prefix[1] = 0x00;
        return [.. prefix, .. bytes];
    }

    private static void WriteAscii(byte[] buffer, int offset, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        Array.Copy(bytes, 0, buffer, offset, bytes.Length);
    }

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    /// <summary>Creates a cabinet whose header signature is wrong or missing.</summary>
    public static MemoryStream CreateDamaged()
    {
        var stream = CreateStored(("a.txt", "a"));
        var bytes = stream.ToArray();
        bytes[0] = (byte)'X';
        return new MemoryStream(bytes);
    }

    /// <summary>Creates a cabinet truncated in the middle of its CFDATA payload.</summary>
    public static MemoryStream CreateTruncated(params (string Name, string Content)[] entries)
    {
        var stream = CreateStored(entries);
        var bytes = stream.ToArray();
        return new MemoryStream(bytes.AsSpan(0, bytes.Length / 2).ToArray());
    }
}
