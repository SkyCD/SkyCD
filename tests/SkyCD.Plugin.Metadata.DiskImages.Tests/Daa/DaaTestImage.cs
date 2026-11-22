using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using DiscUtils.Iso9660;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

/// <summary>Builds real ISO images and wraps them in single or multipart DAA containers for tests.</summary>
internal static class DaaTestImage
{
    public const int ChunkSize = 64 * 1024;
    private const int HeaderLength = 76;
    private const uint SupportedVersion = 0x100;

    public static byte[] CreateIso(params (string Path, string Content)[] files)
    {
        var builder = new CDBuilder { UseJoliet = true };
        foreach (var (path, content) in files)
        {
            var directory = path.Contains('/') ? path[..path.LastIndexOf('/')] : string.Empty;
            if (directory.Length > 0)
            {
                builder.AddDirectory(directory.Replace('/', '\\'));
            }

            builder.AddFile(path.Replace('/', '\\'), Encoding.UTF8.GetBytes(content));
        }

        using var stream = new MemoryStream();
        builder.Build(stream);
        return stream.ToArray();
    }

    public static byte[] CreateSingleVolume(byte[] iso, bool encrypted = false)
    {
        var chunks = CompressChunks(iso);
        return BuildVolume(iso.Length, chunks, chunks, first: true, encrypted);
    }

    public static string WriteSingle(string directory, string fileName, byte[] iso, bool encrypted = false)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllBytes(path, CreateSingleVolume(iso, encrypted));
        return path;
    }

    public static string WriteMultipart(string directory, string baseName, byte[] iso)
    {
        Directory.CreateDirectory(directory);
        var chunks = CompressChunks(iso);
        if (chunks.Count < 2)
        {
            throw new InvalidOperationException("The ISO image is too small to split into two DAA volumes.");
        }

        var split = Math.Max(1, chunks.Count / 2);
        var first = BuildVolume(iso.Length, chunks, chunks.Take(split).ToList(), first: true, encrypted: false);
        var second = BuildVolume(iso.Length, chunks, chunks.Skip(split).ToList(), first: false, encrypted: false);
        var firstPath = Path.Combine(directory, $"{baseName}.part01.daa");
        File.WriteAllBytes(firstPath, first);
        File.WriteAllBytes(Path.Combine(directory, $"{baseName}.part02.daa"), second);
        return firstPath;
    }

    private static List<byte[]> CompressChunks(byte[] iso)
    {
        var chunks = new List<byte[]>();
        for (var offset = 0; offset < iso.Length; offset += ChunkSize)
        {
            var length = Math.Min(ChunkSize, iso.Length - offset);
            chunks.Add(Deflate(iso.AsSpan(offset, length)));
        }

        return chunks;
    }

    private static byte[] Deflate(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var compressor = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] BuildVolume(
        long isoSize,
        IReadOnlyList<byte[]> allChunks,
        IReadOnlyList<byte[]> dataChunks,
        bool first,
        bool encrypted)
    {
        var extensions = new List<byte>();
        if (encrypted)
        {
            var record = new byte[8];
            BinaryPrimitives.WriteUInt32LittleEndian(record, 3);
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), 8);
            extensions.AddRange(record);
        }

        var sizeOffset = HeaderLength + extensions.Count;
        var dataOffset = sizeOffset + allChunks.Count * 3;
        var dataLength = dataChunks.Sum(chunk => chunk.Length);
        var buffer = new byte[dataOffset + dataLength];

        var signature = first ? "DAA" : "DAA VOL";
        Encoding.ASCII.GetBytes(signature).CopyTo(buffer, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16), (uint)sizeOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(20), SupportedVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(24), (uint)dataOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(28), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(32), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(36), ChunkSize);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(40), (ulong)isoSize);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(48), (ulong)buffer.Length);

        extensions.CopyTo(buffer, HeaderLength);

        for (var index = 0; index < allChunks.Count; index++)
        {
            var length = allChunks[index].Length;
            var offset = sizeOffset + index * 3;
            buffer[offset] = (byte)(length >> 16);
            buffer[offset + 1] = (byte)length;
            buffer[offset + 2] = (byte)(length >> 8);
        }

        var position = dataOffset;
        foreach (var chunk in dataChunks)
        {
            chunk.CopyTo(buffer, position);
            position += chunk.Length;
        }

        return buffer;
    }
}
