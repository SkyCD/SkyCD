using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.FileMetadata.Tests;

/// <summary>Writes the subset of the binary property list format Finder's metadata attributes use.</summary>
internal static class BinaryPlistFixtures
{
    public static byte[] ArrayOfStrings(params string[] values)
    {
        using var objects = new MemoryStream();
        var offsets = new List<int> { 0 };
        objects.WriteByte((byte)(0xA0 | values.Length));
        for (var index = 0; index < values.Length; index++)
        {
            objects.WriteByte((byte)(1 + index));
        }

        foreach (var value in values)
        {
            offsets.Add((int)objects.Position);
            var bytes = Encoding.ASCII.GetBytes(value);
            objects.WriteByte((byte)(0x50 | bytes.Length));
            objects.Write(bytes);
        }

        return Wrap(objects.ToArray(), offsets);
    }

    public static byte[] SingleString(string value, bool utf16 = false)
    {
        using var objects = new MemoryStream();
        if (utf16)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(value);
            objects.WriteByte((byte)(0x60 | (bytes.Length / 2)));
            objects.Write(bytes);
        }
        else
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            objects.WriteByte((byte)(0x50 | bytes.Length));
            objects.Write(bytes);
        }

        return Wrap(objects.ToArray(), [0]);
    }

    private static byte[] Wrap(byte[] objects, List<int> offsets)
    {
        using var stream = new MemoryStream();
        stream.Write("bplist00"u8);
        stream.Write(objects);

        // Offset table entries are file-relative; the objects were measured from zero.
        var offsetTable = (int)stream.Position;
        foreach (var offset in offsets)
        {
            stream.WriteByte((byte)(offset + 8));
        }

        stream.Write(new byte[6]);
        stream.WriteByte(1);
        stream.WriteByte(1);
        WriteInt64(stream, offsets.Count);
        WriteInt64(stream, 0);
        WriteInt64(stream, offsetTable);
        return stream.ToArray();
    }

    private static void WriteInt64(Stream stream, long value)
    {
        for (var shift = 56; shift >= 0; shift -= 8)
        {
            stream.WriteByte((byte)(value >> shift));
        }
    }
}
