using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using SkyCD.Plugin.FileFormat.Vdi.Exceptions;
using SkyCD.Plugin.FileFormat.Vdi.Models;

namespace SkyCD.Plugin.FileFormat.Vdi.Parsing;

/// <summary>
/// The managed VirtualBox VDI pre-header and image header. Only the fields needed to map guest sectors
/// to image offsets are read; everything else is validated just far enough to reject images this reader
/// cannot reproduce faithfully.
/// </summary>
internal sealed record VdiHeader
{
    /// <summary>Byte offset of the image header magic that follows the 64-byte pre-header text.</summary>
    public const int ImageHeaderOffset = 0x40;

    /// <summary>The pre-header text every little-endian VDI image starts with.</summary>
    public const string PreHeaderSignature = "<<< Oracle VM VirtualBox Disk Image >>>";

    /// <summary>Length of the pre-header text; the remainder of the pre-header is NUL padding.</summary>
    public const int PreHeaderSignatureLength = 38;

    /// <summary>Magic value at the start of the image header.</summary>
    private const uint ImageSignature = 0xbeda_107f;

    /// <summary>Smallest image header this reader accepts; VirtualBox 1.0 wrote 0x180.</summary>
    private const uint MinImageHeaderSize = 0x180;

    /// <summary>Largest image header this reader accepts, so a damaged size field cannot exhaust memory.</summary>
    private const uint MaxImageHeaderSize = 0x1000;

    /// <summary>Sector sizes are either 512 or 4096 in VirtualBox; only 512 is supported here.</summary>
    public const int SupportedSectorSize = 512;

    /// <summary>Byte offset of a fixed image's guest data; follows the 512-byte header block.</summary>
    public const int FixedDataOffset = 0x200;

    private const int VersionOffset = 0x44;
    private const int HeaderSizeOffset = 0x48;
    private const int ImageTypeOffset = 0x4C;
    private const int ImageFlagsOffset = 0x50;
    private const int OffsetsOffsetOffset = 0x154;
    private const int DataOffsetOffset = 0x158;
    private const int SectorSizeOffset = 0x168;
    private const int DiskSizeOffset = 0x170;
    private const int BlockSizeOffset = 0x178;

    private VdiHeader(uint version, byte[] raw, uint imageType, uint imageFlags, int sectorSize,
        long diskSize, uint blockSize, uint offsetsOffset, uint dataOffset)
    {
        Version = version;
        Raw = raw;
        ImageType = imageType;
        ImageFlags = imageFlags;
        SectorSize = sectorSize;
        DiskSize = diskSize;
        BlockSize = blockSize;
        OffsetsOffset = offsetsOffset;
        DataOffset = dataOffset;
    }

    /// <summary>Gets the header version; 0x00010001 is VirtualBox 1.0 and 0x00010002 is 1.2 and later.</summary>
    public uint Version { get; }

    /// <summary>Gets the declared image header size in bytes.</summary>
    public uint HeaderSize => (uint)Raw.Length;

    /// <summary>Gets the image type: static (fixed) or dynamic.</summary>
    public uint ImageType { get; }

    /// <summary>Gets the image flags, including the snapshot base and encryption markers.</summary>
    public uint ImageFlags { get; }

    /// <summary>Gets the guest sector size; always <see cref="SupportedSectorSize"/>.</summary>
    public int SectorSize { get; }

    /// <summary>Gets the guest disk size in bytes.</summary>
    public long DiskSize { get; }

    /// <summary>Gets the virtual block size in bytes; set only for dynamic images.</summary>
    public uint BlockSize { get; }

    /// <summary>Gets the byte offset of the block map table; set only for dynamic images.</summary>
    public uint OffsetsOffset { get; }

    /// <summary>Gets the byte offset of the first data block; set only for dynamic images.</summary>
    public uint DataOffset { get; }

    /// <summary>Gets whether the image is fixed (static) rather than dynamically allocated.</summary>
    public bool IsFixed => ImageType == VdiImageType.Fixed;

    /// <summary>Gets whether the image is a snapshot or differencing layer that needs its base image.</summary>
    public bool NeedsParent => (ImageFlags & VdiImageFlags.UsesBase) != 0
                               || (ImageFlags & VdiImageFlags.UsesBaseSnapshotToo) != 0;

    /// <summary>Gets the number of guest sectors that guest I/O may address.</summary>
    public long SectorCount => DiskSize / SectorSize;

    /// <summary>Gets the number of block map entries for a dynamic image.</summary>
    public long BlockCount
    {
        get
        {
            if (IsFixed)
            {
                return 0;
            }

            var sectorsPerBlock = BlockSize / (long)SectorSize;
            return (SectorCount + sectorsPerBlock - 1) / sectorsPerBlock;
        }
    }

    /// <summary>Gets the raw image header bytes; parent UUID fields live here.</summary>
    private byte[] Raw { get; }

    /// <summary>
    /// Reads and validates the header at the start of a seekable stream, leaving the stream position
    /// where it was. A snapshot layer is reported as missing its parent, an encrypted image or a future
    /// version as unsupported, and an inconsistent header as damaged.
    /// </summary>
    public static VdiHeader Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new VdiUnsupportedImageException();
        }

        var originalPosition = stream.Position;
        try
        {
            return ReadAtStart(stream);
        }
        catch (EndOfStreamException)
        {
            throw new VdiDamagedImageException();
        }
        finally
        {
            try
            {
                stream.Position = originalPosition;
            }
            catch (IOException)
            {
                // The stream is already unusable; the caller reports the image through the thrown error.
            }
        }
    }

    private static VdiHeader ReadAtStart(Stream stream)
    {
        if (stream.Length < ImageHeaderOffset + MinImageHeaderSize)
        {
            throw new VdiDamagedImageException();
        }

        stream.Position = 0;
        var preHeader = ReadExactly(stream, ImageHeaderOffset);
        if (!HasValidPreHeader(preHeader))
        {
            throw new VdiUnsupportedImageException();
        }

        var magicAndSize = ReadExactly(stream, 12);
        if (BinaryPrimitives.ReadUInt32LittleEndian(magicAndSize) != ImageSignature)
        {
            throw new VdiUnsupportedImageException();
        }

        var declaredSize = BinaryPrimitives.ReadUInt32LittleEndian(magicAndSize.AsSpan(8));
        if (declaredSize < MinImageHeaderSize || declaredSize > MaxImageHeaderSize)
        {
            throw new VdiDamagedImageException();
        }

        if (stream.Length < ImageHeaderOffset + declaredSize)
        {
            throw new VdiDamagedImageException();
        }

        stream.Position = ImageHeaderOffset;
        return Parse(ReadExactly(stream, (int)declaredSize), stream.Length);
    }

    private static bool HasValidPreHeader(byte[] preHeader)
    {
        var signature = Encoding.ASCII.GetBytes(PreHeaderSignature);
        if (preHeader.Length < ImageHeaderOffset)
        {
            return false;
        }

        for (var index = 0; index < signature.Length; index++)
        {
            if (preHeader[index] != signature[index])
            {
                return false;
            }
        }

        return true;
    }

    private static VdiHeader Parse(byte[] header, long length)
    {
        if (header.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(header) != ImageSignature)
        {
            throw new VdiUnsupportedImageException();
        }

        var version = ReadUInt32(header, VersionOffset - ImageHeaderOffset);
        var imageType = ReadUInt32(header, ImageTypeOffset - ImageHeaderOffset);
        var imageFlags = ReadUInt32(header, ImageFlagsOffset - ImageHeaderOffset);
        var offsetsOffset = ReadUInt32(header, OffsetsOffsetOffset - ImageHeaderOffset);
        var dataOffset = ReadUInt32(header, DataOffsetOffset - ImageHeaderOffset);
        var sectorSize = (int)ReadUInt32(header, SectorSizeOffset - ImageHeaderOffset);
        var diskSize = (long)ReadUInt64(header, DiskSizeOffset - ImageHeaderOffset);
        var blockSize = ReadUInt32(header, BlockSizeOffset - ImageHeaderOffset);

        if (version != VdiImageType.Version1002)
        {
            throw new VdiUnsupportedImageException();
        }

        if (imageType != VdiImageType.Dynamic && imageType != VdiImageType.Fixed)
        {
            throw new VdiUnsupportedImageException();
        }

        if ((imageFlags & VdiImageFlags.Encrypted) != 0)
        {
            throw new VdiUnsupportedImageException();
        }

        if (sectorSize != SupportedSectorSize)
        {
            throw new VdiUnsupportedImageException();
        }

        if (diskSize <= 0 || diskSize % sectorSize != 0)
        {
            throw new VdiDamagedImageException();
        }

        var parsed = new VdiHeader(version, header, imageType, imageFlags, sectorSize, diskSize,
            blockSize, offsetsOffset, dataOffset);
        parsed.Validate(length);
        return parsed;
    }

    private void Validate(long length)
    {
        if (NeedsParent)
        {
            throw new VdiMissingParentException();
        }

        if (IsFixed)
        {
            var end = (long)FixedDataOffset + DiskSize;
            if (end > length)
            {
                throw new VdiDamagedImageException();
            }

            return;
        }

        if (BlockSize == 0 || BlockSize % SectorSize != 0)
        {
            throw new VdiDamagedImageException();
        }

        var mapStart = (long)OffsetsOffset;
        var dataStart = (long)DataOffset;
        if (mapStart < FixedDataOffset || dataStart <= mapStart)
        {
            throw new VdiDamagedImageException();
        }

        var blockCount = BlockCount;
        if (blockCount <= 0)
        {
            throw new VdiDamagedImageException();
        }

        var mapEnd = mapStart + blockCount * 4;
        if (mapEnd > length || mapEnd > dataStart || dataStart > length)
        {
            throw new VdiDamagedImageException();
        }
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        var read = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
        if (read != count)
        {
            throw new VdiDamagedImageException();
        }

        return buffer;
    }

    private static uint ReadUInt32(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(offset, 4));

    private static ulong ReadUInt64(byte[] buffer, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(offset, 8));
}
