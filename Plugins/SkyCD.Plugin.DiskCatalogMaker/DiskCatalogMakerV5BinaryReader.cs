using System;
using SkyCD.Plugin.DiskCatalogMaker.Exceptions;

namespace SkyCD.Plugin.DiskCatalogMaker;

/// <summary>
/// Little-endian reader over an in-memory catalog payload that validates every
/// offset and length before slicing or allocating.
/// </summary>
internal sealed class DiskCatalogMakerV5BinaryReader(byte[] buffer, string sourceDescription)
{
    private int position;

    public int Length => buffer.Length;

    public int Position => position;

    public int Remaining => buffer.Length - position;

    public void Seek(int offset, string context)
    {
        EnsureAt(offset, 0, context);
        position = offset;
    }

    public void Skip(int count, string context)
    {
        EnsureAt(position, count, context);
        position += count;
    }

    public byte ReadByte(string context)
    {
        EnsureAt(position, 1, context);
        return buffer[position++];
    }

    public ushort ReadUInt16(string context)
    {
        EnsureAt(position, sizeof(ushort), context);
        var value = (ushort)(buffer[position] | (buffer[position + 1] << 8));
        position += sizeof(ushort);
        return value;
    }

    public uint ReadUInt32(string context)
    {
        EnsureAt(position, sizeof(uint), context);
        var value = (uint)(buffer[position]
                           | (buffer[position + 1] << 8)
                           | (buffer[position + 2] << 16)
                           | (buffer[position + 3] << 24));
        position += sizeof(uint);
        return value;
    }

    public ulong ReadUInt64(string context)
    {
        var low = ReadUInt32(context);
        var high = ReadUInt32(context);
        return low | ((ulong)high << 32);
    }

    public long ReadInt64(string context)
    {
        return unchecked((long)ReadUInt64(context));
    }

    public ReadOnlySpan<byte> ReadSpan(int count, string context)
    {
        EnsureAt(position, count, context);
        var span = new ReadOnlySpan<byte>(buffer, position, count);
        position += count;
        return span;
    }

    public byte[] ReadBytes(int count, string context)
    {
        return ReadSpan(count, context).ToArray();
    }

    public void EnsureAt(int offset, int count, string context)
    {
        if (offset < 0 || count < 0)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"{context} requested a negative offset or length (offset {offset}, length {count}) in '{sourceDescription}'.");
        }

        var end = (long)offset + count;
        if (end > Length)
        {
            throw new DiskCatalogMakerV5CorruptCatalogException(
                $"{context} at offset {offset} requests {count} byte(s), but '{sourceDescription}' is only {Length} byte(s) long.");
        }
    }
}
