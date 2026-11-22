using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using SkyCD.Plugin.Metadata.DiskImages.Qcow2.Exceptions;

namespace SkyCD.Plugin.Metadata.DiskImages.Qcow2.Parsing;

/// <summary>
/// The fields of a QCOW2 header that this read-only reader needs. Header extensions only matter for
/// detecting features the reader cannot interpret.
/// </summary>
internal sealed record Qcow2Header(
    int Version,
    int ClusterBits,
    long VirtualSize,
    uint CryptMethod,
    uint L1Size,
    long L1TableOffset,
    string? BackingFileName);

internal static class Qcow2HeaderReader
{
    private const uint QcowMagic = 0x514649fb;
    private const uint ExternalDataFileExtension = 0x44415441;
    private const int MaxBackingFileNameBytes = 1023;
    private const int MaxExtensions = 64;

    // Bits 0 (dirty) and 1 (corrupt) neither change the layout nor block reading.
    private const ulong ReadableIncompatibleFeatures = 0b11;

    public static Qcow2Header Read(Stream stream)
    {
        if (!stream.CanSeek)
        {
            throw new Qcow2DamagedImageException();
        }

        var fileLength = stream.Length;
        if (fileLength < 72)
        {
            throw new Qcow2DamagedImageException();
        }

        var headLength = (int)Math.Min(fileLength, 108);
        var head = new byte[headLength];
        stream.Position = 0;
        ReadExactly(stream, head);

        if (BinaryPrimitives.ReadUInt32BigEndian(head) != QcowMagic)
        {
            throw new Qcow2DamagedImageException();
        }

        var version = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4));
        if (version is not (2 or 3))
        {
            throw new Qcow2UnsupportedImageException();
        }

        var backingFileOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(8));
        var backingFileSize = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(16));
        var clusterBits = (int)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(20));
        if (clusterBits is < 9 or > 21)
        {
            throw new Qcow2UnsupportedImageException();
        }

        var virtualSize = (long)BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(24));
        if (virtualSize < 0)
        {
            throw new Qcow2DamagedImageException();
        }

        var cryptMethod = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(32));
        var l1Size = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(36));
        var l1TableOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(40));

        var headerLength = 72;
        if (version >= 3)
        {
            if (head.Length < 104)
            {
                throw new Qcow2DamagedImageException();
            }

            var incompatibleFeatures = BinaryPrimitives.ReadUInt64BigEndian(head.AsSpan(72));
            headerLength = (int)BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(100));
            if (headerLength < 104 || headerLength % 8 != 0 || headerLength > fileLength)
            {
                throw new Qcow2DamagedImageException();
            }

            if ((incompatibleFeatures & ~ReadableIncompatibleFeatures) != 0)
            {
                throw new Qcow2UnsupportedImageException();
            }

            if (headerLength >= 108 && BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(104)) != 0)
            {
                // Non-zero compression type means zstd or a future codec.
                throw new Qcow2UnsupportedImageException();
            }
        }

        if (cryptMethod != 0)
        {
            throw new Qcow2EncryptedImageException();
        }

        ValidateTable(stream, l1TableOffset, (long)l1Size * 8, clusterBits, virtualSize);
        ValidateExtensions(stream, headerLength, clusterBits, fileLength);

        var backingFileName = ReadBackingFileName(stream, backingFileOffset, backingFileSize, headerLength,
            clusterBits, fileLength);

        return new Qcow2Header((int)version, clusterBits, virtualSize, cryptMethod, l1Size, l1TableOffset,
            backingFileName);
    }

    private static void ValidateTable(Stream stream, long offset, long length, int clusterBits, long virtualSize)
    {
        if (virtualSize == 0)
        {
            return;
        }

        var clusterSize = 1L << clusterBits;
        if (offset <= 0 || offset % clusterSize != 0 || length <= 0 || offset > stream.Length - length)
        {
            throw new Qcow2DamagedImageException();
        }
    }

    private static void ValidateExtensions(Stream stream, int start, int clusterBits, long fileLength)
    {
        var clusterSize = 1L << clusterBits;
        var limit = Math.Min(clusterSize, fileLength);
        var position = (long)start;
        var extension = new byte[8];

        for (var index = 0; index < MaxExtensions; index++)
        {
            if (position + extension.Length > limit)
            {
                throw new Qcow2DamagedImageException();
            }

            stream.Position = position;
            ReadExactly(stream, extension);
            var type = BinaryPrimitives.ReadUInt32BigEndian(extension);
            var length = BinaryPrimitives.ReadUInt32BigEndian(extension.AsSpan(4));
            position += extension.Length;

            if (type == 0)
            {
                return;
            }

            if (length > limit - position)
            {
                throw new Qcow2DamagedImageException();
            }

            if (type == ExternalDataFileExtension)
            {
                throw new Qcow2UnsupportedImageException();
            }

            position += (length + 7L) & ~7L;
        }

        throw new Qcow2DamagedImageException();
    }

    private static string? ReadBackingFileName(Stream stream, long offset, uint size, int headerLength,
        int clusterBits, long fileLength)
    {
        if (size == 0)
        {
            return null;
        }

        var clusterSize = 1L << clusterBits;
        if (size > MaxBackingFileNameBytes || offset < headerLength || offset + size > Math.Min(clusterSize, fileLength))
        {
            throw new Qcow2DamagedImageException();
        }

        var bytes = new byte[size];
        stream.Position = offset;
        ReadExactly(stream, bytes);
        var name = Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim();
        if (name.Length == 0 || name.Any(char.IsControl))
        {
            throw new Qcow2DamagedImageException();
        }

        return name;
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                throw new Qcow2DamagedImageException();
            }

            total += read;
        }
    }
}
