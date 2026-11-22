using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Streams;

namespace SkyCD.Plugin.FileFormat.Vdi.Tests;

/// <summary>
/// Builds small VirtualBox VDI images around a FAT volume so tests can exercise the reader without
/// shipping large binary fixtures or depending on VirtualBox. The writer is test-only and follows the
/// documented VirtualBox image header layout so it never reuses the reader under test.
/// </summary>
internal static class VdiTestImage
{
    private const int SectorSize = 512;
    private const int TextOffset = 0x00;
    private const int SignatureOffset = 0x40;
    private const int VersionOffset = 0x44;
    private const int HeaderSizeOffset = 0x48;
    private const int ImageTypeOffset = 0x4C;
    private const int ImageFlagsOffset = 0x50;
    private const int OffsetsOffsetOffset = 0x154;
    private const int DataOffsetOffset = 0x158;
    private const int SectorSizeOffset = 0x168;
    private const int DiskSizeOffset = 0x170;
    private const int BlockSizeOffset = 0x178;
    private const int BlocksOffset = 0x180;
    private const int BlocksAllocatedOffset = 0x184;
    private const int CreateUuidOffset = 0x188;
    private const int ModifyUuidOffset = 0x198;
    private const int HeaderSize = 0x190;
    private const int DataOffset = 0x200;
    private const long BlockSize = 1024 * 1024;
    private const uint UnallocatedBlock = 0xffff_ffff;
    private const uint Magic = 0xbeda_107f;
    private const uint StaticType = 1;
    private const uint DynamicType = 2;
    private const uint Version1002 = 0x0001_0002;
    private const uint EncryptedFlag = 0x0000_0004;

    /// <summary>Creates a FAT volume holding the supplied files, creating parent folders as needed.</summary>
    public static byte[] CreateFatVolume(params (string Path, string Content)[] files)
    {
        using var volume = new MemoryStream();
        FatFileSystem.FormatFloppy(volume, FloppyDiskType.DoubleDensity, "DATA");
        volume.Position = 0;
        using (var fileSystem = new FatFileSystem(volume, Ownership.None))
        {
            foreach (var (path, content) in files)
            {
                var normalized = path.Replace('\\', '/');
                var segments = normalized.Split('/');
                var current = string.Empty;
                for (var index = 0; index < segments.Length - 1; index++)
                {
                    current = current.Length == 0 ? segments[index] : $"{current}/{segments[index]}";
                    if (!fileSystem.DirectoryExists(current))
                    {
                        fileSystem.CreateDirectory(current);
                    }
                }

                using var writer = new BinaryWriter(
                    fileSystem.OpenFile(normalized.Replace('/', '\\'), FileMode.Create, FileAccess.Write));
                writer.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return volume.ToArray();
    }

    /// <summary>Builds a dynamically allocated VDI image whose guest disk holds the supplied bytes.</summary>
    public static byte[] BuildDynamic(byte[] diskContent) =>
        Build(diskContent ?? throw new ArgumentNullException(nameof(diskContent)), DynamicType, imageFlags: 0);

    /// <summary>Builds a fixed (static) VDI image whose guest disk holds the supplied bytes.</summary>
    public static byte[] BuildFixed(byte[] diskContent) =>
        Build(diskContent ?? throw new ArgumentNullException(nameof(diskContent)), StaticType, imageFlags: 0);

    /// <summary>Builds a dynamic image with explicit header flags and version so rejection paths are reachable.</summary>
    public static byte[] BuildWithHeader(uint imageFlags, uint version = Version1002, bool encrypted = false) =>
        Build(CreateFatVolume(("NOTES.TXT", "content")), DynamicType,
            imageFlags | (encrypted ? EncryptedFlag : 0), version);

    /// <summary>Builds an image whose header declares a sector size the plugin does not support.</summary>
    public static byte[] BuildWithSectorSize(uint sectorSize) =>
        Build(CreateFatVolume(("NOTES.TXT", "content")), DynamicType, imageFlags: 0, sectorSize: sectorSize);

    private static byte[] Build(byte[] diskContent, uint imageType, uint imageFlags,
        uint version = Version1002, uint sectorSize = SectorSize)
    {
        var diskSize = RoundUp(diskContent.Length, SectorSize);
        if (imageType == DynamicType)
        {
            // Dynamic images address whole blocks, so the disk size is rounded up to the block size.
            diskSize = RoundUp(diskSize, BlockSize);
        }

        var allocatedBytes = imageType == StaticType ? diskSize : RoundUp(diskContent.Length, BlockSize);
        var blockCount = imageType == StaticType ? 0 : (int)(diskSize / BlockSize);
        var allocatedBlocks = imageType == StaticType ? 0 : (int)(allocatedBytes / BlockSize);

        long offsetsOffset = 0;
        long dataOffset = DataOffset;
        if (imageType == DynamicType)
        {
            offsetsOffset = DataOffset;
            dataOffset = offsetsOffset + RoundUp(blockCount * 4L, SectorSize);
        }

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(CreateHeader(imageType, imageFlags, version, sectorSize, diskSize, blockCount,
                allocatedBlocks, offsetsOffset, dataOffset));
            writer.Flush();

            if (imageType == StaticType)
            {
                writer.Write(diskContent);
                writer.Write(new byte[diskSize - diskContent.Length]);
                writer.Flush();
                return stream.ToArray();
            }

            stream.Position = offsetsOffset;
            for (var block = 0; block < blockCount; block++)
            {
                var entry = new byte[4];
                var allocated = block < allocatedBlocks;
                BinaryPrimitives.WriteUInt32LittleEndian(entry, allocated ? (uint)block : UnallocatedBlock);
                writer.Write(entry);
            }

            // The block map is padded to a sector boundary before the first data block.
            writer.Write(new byte[dataOffset - stream.Position]);
            writer.Flush();
            stream.Position = dataOffset;
            writer.Write(diskContent);
            writer.Write(new byte[allocatedBytes - diskContent.Length]);
            writer.Flush();
        }

        return stream.ToArray();
    }

    private static byte[] CreateHeader(uint imageType, uint imageFlags, uint version, uint sectorSize,
        long diskSize, int blockCount, int allocatedBlocks, long offsetsOffset, long dataOffset)
    {
        // The header block spans the 64-byte pre-header text, the image header at 0x40, and padding
        // up to the first sector boundary, which is where guest data starts.
        var header = new byte[DataOffset];
        Encoding.ASCII.GetBytes("<<< Oracle VM VirtualBox Disk Image >>>\n").CopyTo(header, TextOffset);
        WriteUInt32(header, SignatureOffset, Magic);
        WriteUInt32(header, VersionOffset, version);
        WriteUInt32(header, HeaderSizeOffset, HeaderSize);
        WriteUInt32(header, ImageTypeOffset, imageType);
        WriteUInt32(header, ImageFlagsOffset, imageFlags);
        WriteUInt32(header, SectorSizeOffset, sectorSize);
        WriteUInt64(header, DiskSizeOffset, diskSize);
        WriteUInt32(header, BlockSizeOffset, (uint)BlockSize);
        WriteUInt32(header, BlocksOffset, blockCount);
        WriteUInt32(header, BlocksAllocatedOffset, allocatedBlocks);

        if (imageType == DynamicType)
        {
            WriteUInt32(header, OffsetsOffsetOffset, offsetsOffset);
            WriteUInt32(header, DataOffsetOffset, dataOffset);
        }

        var createUuid = new byte[16];
        Random.Shared.NextBytes(createUuid);
        createUuid.CopyTo(header, CreateUuidOffset);
        var modifyUuid = new byte[16];
        Random.Shared.NextBytes(modifyUuid);
        modifyUuid.CopyTo(header, ModifyUuidOffset);

        return header;
    }

    private static void WriteUInt32(byte[] buffer, int offset, long value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset, 4), (uint)value);

    private static void WriteUInt64(byte[] buffer, int offset, long value) =>
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(offset, 8), (ulong)value);

    private static long RoundUp(long value, long unit) => (value + unit - 1) / unit * unit;
}
