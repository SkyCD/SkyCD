using System;
using System.Buffers.Binary;
using System.IO;
using SkyCD.Plugin.AbeMeda.Native.Exceptions;

namespace SkyCD.Plugin.AbeMeda.Native.Parsing;

/// <summary>
/// Bounded little-endian reader over a seekable source. Every access is checked
/// against the known file length before any allocation, and lengths are never
/// taken from untrusted header values without limits.
/// </summary>
internal sealed class AbeMedaNativeBinaryReader(Stream stream, long length)
{
    public long Length { get; } = length;

    public uint ReadUInt32(long offset)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactly(offset, buffer);
        return BinaryPrimitives.ReadUInt32LittleEndian(buffer);
    }

    public ulong ReadUInt64(long offset)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactly(offset, buffer);
        return BinaryPrimitives.ReadUInt64LittleEndian(buffer);
    }

    public byte[] ReadBytes(long offset, int count)
    {
        EnsureRange(offset, count);
        var buffer = new byte[count];
        ReadExactly(offset, buffer);
        return buffer;
    }

    private void ReadExactly(long offset, Span<byte> buffer)
    {
        EnsureRange(offset, buffer.Length);

        stream.Position = offset;
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                throw new AbeMedaNativeCorruptCatalogException(
                    $"unexpected end of file at offset 0x{offset + total:X}.");
            }

            total += read;
        }
    }

    private void EnsureRange(long offset, int count)
    {
        if (offset < 0 || count < 0 || (ulong)offset + (ulong)count > (ulong)Length)
        {
            throw new AbeMedaNativeCorruptCatalogException(
                $"read of {count} bytes at offset 0x{offset:X} exceeds the file length {Length}.");
        }
    }
}
