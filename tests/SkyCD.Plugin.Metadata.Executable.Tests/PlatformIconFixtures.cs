using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using DiscUtils.Iso9660;

namespace SkyCD.Plugin.Metadata.Executable.Tests;

/// <summary>
/// Builds the platform containers the icon plugin reads: an <c>.icns</c> chunk list, an ISO 9660 volume, and
/// the ELF, Mach-O and AppImage prefixes that decide how a source is classified.
/// </summary>
internal static class PlatformIconFixtures
{
    /// <summary>One <c>.icns</c> chunk: its four-character type and the payload it carries.</summary>
    public static (string Type, byte[] Payload) Chunk(string type, byte[] payload) => (type, payload);

    /// <summary>An <c>.icns</c> container with the given chunks in declaration order.</summary>
    public static byte[] Icns(params (string Type, byte[] Payload)[] chunks)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("icns"u8);
        writer.Write(0);
        foreach (var (type, payload) in chunks)
        {
            writer.Write(Encoding.ASCII.GetBytes(type));
            writer.Write(BinaryPrimitives.ReverseEndianness(8 + payload.Length));
            writer.Write(payload);
        }

        var data = stream.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), (uint)data.Length);
        return data;
    }

    /// <summary>An ISO 9660 volume holding the given files, keyed by their volume-relative path.</summary>
    public static byte[] Iso9660(params (string Path, byte[] Data)[] files)
    {
        var builder = new CDBuilder { UseJoliet = true };
        foreach (var (path, data) in files)
        {
            builder.AddFile(path, data);
        }

        using var stream = new MemoryStream();
        builder.Build(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// An AppImage: a small ELF runtime carrying the AppImage marker followed by the packed volume.
    /// </summary>
    public static byte[] AppImage(byte[] volume, byte type = 1, int runtimeBytes = 12 * 1024)
    {
        var image = new byte[runtimeBytes + volume.Length];
        image[0] = 0x7F;
        image[1] = (byte)'E';
        image[2] = (byte)'L';
        image[3] = (byte)'F';
        image[4] = 2;
        image[5] = 1;
        image[6] = 1;
        image[8] = (byte)'A';
        image[9] = (byte)'I';
        image[10] = type;
        volume.CopyTo(image, runtimeBytes);
        return image;
    }

    /// <summary>An ELF image without the AppImage marker, as a plain Linux program looks.</summary>
    public static byte[] Elf()
    {
        var image = new byte[128];
        image[0] = 0x7F;
        image[1] = (byte)'E';
        image[2] = (byte)'L';
        image[3] = (byte)'F';
        image[4] = 2;
        image[5] = 1;
        image[6] = 1;
        return image;
    }

    /// <summary>A little-endian 64-bit Mach-O header, as a plain macOS program looks.</summary>
    public static byte[] MachO() => [0xCF, 0xFA, 0xED, 0xFE, 0x07, 0x00, 0x00, 0x01, 0, 0, 0, 0];
}
