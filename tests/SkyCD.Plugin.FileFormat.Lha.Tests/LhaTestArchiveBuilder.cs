using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Hst.Compression.Lha;

namespace SkyCD.Plugin.FileFormat.Lha.Tests;

/// <summary>
/// Builds small LHA archives with the Hst.Compression writer so tests do not depend on external tools.
/// </summary>
internal static class LhaTestArchiveBuilder
{
    private static readonly DateTime ModifiedUtc = new(2024, 1, 2, 3, 4, 6, DateTimeKind.Utc);

    public static MemoryStream CreateStored(params (string Path, string Content)[] entries) =>
        Create(LhaMethod.Lh0, entries);

    public static MemoryStream CreateCompressed(params (string Path, string Content)[] entries) =>
        Create(LhaMethod.Lh5, entries);

    public static MemoryStream CreateUnsupportedMethod(params (string Path, string Content)[] entries) =>
        RewriteMethod(Create(LhaMethod.Lh0, headerLevel: 0, genericFormat: true, entries), "-lh2-");

    public static MemoryStream CreateDamaged() =>
        new(Encoding.ASCII.GetBytes("not an lha archive"));

    /// <summary>
    /// Writes level 0 headers manually because the writer sanitizes entry names, and unsafe names must be
    /// preserved to exercise the plugin's path validation.
    /// </summary>
    public static MemoryStream CreateRawStored(params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        foreach (var (path, content) in entries)
        {
            var data = Encoding.UTF8.GetBytes(content);
            var name = Encoding.UTF8.GetBytes(path);
            var headerSize = 22 + name.Length;
            var header = new byte[2 + headerSize];

            header[0] = (byte)headerSize;
            Encoding.ASCII.GetBytes("-lh0-").CopyTo(header, 2);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(7), data.Length);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(11), data.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(15), DosTimestamp(ModifiedUtc));
            header[19] = 0x20;
            header[20] = 0x00;
            header[21] = (byte)name.Length;
            name.CopyTo(header, 22);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22 + name.Length), Crc16(data));

            var checksum = 0;
            for (var index = 2; index < header.Length; index++)
            {
                checksum += header[index];
            }

            header[1] = (byte)(checksum & 0xff);
            stream.Write(header);
            stream.Write(data);
        }

        stream.WriteByte(0);
        stream.Position = 0;
        return stream;
    }

    private static uint DosTimestamp(DateTime utc)
    {
        var date = (uint)((utc.Year - 1980) << 9 | utc.Month << 5 | utc.Day);
        var time = (uint)(utc.Hour << 11 | utc.Minute << 5 | utc.Second / 2);
        return date << 16 | time;
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        return crc;
    }

    private static MemoryStream Create(string method, (string Path, string Content)[] entries) =>
        Create(method, headerLevel: 2, genericFormat: false, entries);

    private static MemoryStream Create(string method, int headerLevel, bool genericFormat,
        (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var writer = new LhaWriter(stream, new LhaWriterOptions
        {
            Method = method,
            HeaderLevel = headerLevel,
            GenericFormat = genericFormat
        }, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                if (path.EndsWith("/", StringComparison.Ordinal) || path.EndsWith("\\", StringComparison.Ordinal))
                {
                    writer.AddDirectory(path, ModifiedUtc);
                    continue;
                }

                using var data = new MemoryStream(Encoding.UTF8.GetBytes(content));
                writer.AddFile(path, data, ModifiedUtc, method);
            }
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Replaces the method of the first level 0 header with an unsupported one and fixes its checksum.
    /// Listing still works because only headers are parsed; extraction has no decoder for the method.
    /// </summary>
    private static MemoryStream RewriteMethod(MemoryStream archive, string method)
    {
        var bytes = archive.ToArray();
        archive.Dispose();

        var headerSize = bytes[0];
        Encoding.ASCII.GetBytes(method).CopyTo(bytes, 2);

        var checksum = 0;
        for (var index = 2; index < headerSize + 2; index++)
        {
            checksum += bytes[index];
        }

        bytes[1] = (byte)(checksum & 0xff);
        return new MemoryStream(bytes);
    }
}
