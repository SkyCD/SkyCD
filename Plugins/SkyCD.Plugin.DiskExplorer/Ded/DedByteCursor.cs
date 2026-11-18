using System;
using SkyCD.Plugin.DiskExplorer.Ded.Exceptions;

namespace SkyCD.Plugin.DiskExplorer.Ded;

/// <summary>
/// Bounds-checked reader over one serialized object record. Every count and
/// length is validated against the remaining bytes before it is used.
/// </summary>
internal sealed class DedByteCursor(byte[] buffer, DedReadOptions options)
{
    private int position;

    public int Position => position;

    public int Remaining => buffer.Length - position;

    public bool IsComplete => position == buffer.Length;

    public uint ReadUInt32(string field)
    {
        Require(4, field);
        var value = (uint)(buffer[position]
            | (buffer[position + 1] << 8)
            | (buffer[position + 2] << 16)
            | (buffer[position + 3] << 24));
        position += 4;
        return value;
    }

    public ulong ReadUInt64(string field)
    {
        Require(8, field);
        ulong value = 0;
        for (var i = 0; i < 8; i++)
        {
            value |= (ulong)buffer[position + i] << (8 * i);
        }

        position += 8;
        return value;
    }

    public byte[] ReadBytes(int count, string field)
    {
        if (count < 0)
        {
            throw new DedCorruptException($"{field} has a negative length.");
        }

        Require(count, field);
        var value = new byte[count];
        Array.Copy(buffer, position, value, 0, count);
        position += count;
        return value;
    }

    public int ReadCount(string field, int minimumEntrySize, int limit)
    {
        var count = ReadUInt32(field);
        if ((ulong)count * (ulong)minimumEntrySize > (ulong)Remaining)
        {
            throw new DedCorruptException($"{field} of {count} entries exceeds the record bounds.");
        }

        if (count > (uint)limit)
        {
            throw new DedLimitExceededException(field, count);
        }

        return (int)count;
    }

    public int ReadLength(string field, int limit)
    {
        var length = ReadUInt32(field);
        if (length > (uint)limit)
        {
            throw new DedLimitExceededException(field, length);
        }

        if (length > (uint)Remaining)
        {
            throw new DedCorruptException($"{field} of {length} bytes exceeds the record bounds.");
        }

        return (int)length;
    }

    public int ReadStringLength(string field) => ReadLength(field, options.MaxStringBytes);

    public int ReadPayloadLength(string field) => ReadLength(field, options.MaxPayloadBytes);

    public void RequireComplete()
    {
        if (!IsComplete)
        {
            throw new DedCorruptException($"object record has {Remaining} unexpected trailing bytes.");
        }
    }

    private void Require(int count, string field)
    {
        if (count > Remaining)
        {
            throw new DedCorruptException($"{field} extends beyond the object record.");
        }
    }
}
