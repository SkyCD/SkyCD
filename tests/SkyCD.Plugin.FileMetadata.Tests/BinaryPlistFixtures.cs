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
            WriteStringObject(objects, value);
        }

        return Wrap(objects.ToArray(), offsets);
    }

    public static byte[] SingleString(string value, bool utf16 = false)
    {
        using var objects = new MemoryStream();
        WriteStringObject(objects, value, utf16);

        return Wrap(objects.ToArray(), [0]);
    }

    /// <summary>
    /// Writes an ASCII or UTF-16 string object. Counts below 15 sit in the marker's low nibble; larger counts
    /// use the 0xF escape followed by an integer object, because <c>0x50 | 16</c> would collide with the
    /// UTF-16 marker <c>0x60</c>.
    /// </summary>
    private static void WriteStringObject(Stream stream, string value, bool utf16 = false)
    {
        var bytes = utf16 ? Encoding.BigEndianUnicode.GetBytes(value) : Encoding.ASCII.GetBytes(value);
        var count = utf16 ? bytes.Length / 2 : bytes.Length;
        var marker = (byte)(utf16 ? 0x60 : 0x50);
        if (count < 15)
        {
            stream.WriteByte((byte)(marker | count));
        }
        else
        {
            stream.WriteByte((byte)(marker | 0x0F));
            WriteIntegerObject(stream, count);
        }

        stream.Write(bytes);
    }

    private static void WriteIntegerObject(Stream stream, int value)
    {
        if (value <= byte.MaxValue)
        {
            stream.WriteByte(0x10);
            stream.WriteByte((byte)value);
        }
        else
        {
            stream.WriteByte(0x11);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }
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
