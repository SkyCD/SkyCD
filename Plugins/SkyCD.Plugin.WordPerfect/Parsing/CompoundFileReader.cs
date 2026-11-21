using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.WordPerfect.Exceptions;

namespace SkyCD.Plugin.WordPerfect.Parsing;

/// <summary>
/// Reads one stream out of an OLE2 compound file held in memory.
/// </summary>
/// <remarks>
/// WordPerfect 7 and later wrap the document in a compound file and keep the WordPerfect stream under
/// <c>PerfectOffice_MAIN</c>. This reader exists instead of a package dependency because the usual .NET
/// compound-file library is MPL-2.0, which the repository's BSD-2-Clause outbound policy does not accept.
/// Only the read path is implemented, every declared size is checked against the bytes actually present, and
/// chain traversal is bounded by the sector count, so a damaged header cannot cause an unbounded walk.
/// </remarks>
internal static class CompoundFileReader
{
    private const int HeaderBytes = 512;
    private const int DirectoryEntryBytes = 128;
    private const int MiniSectorBytes = 64;

    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint DifatSector = 0xFFFFFFFC;

    private static readonly byte[] Signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= Signature.Length && data[..Signature.Length].SequenceEqual(Signature);

    public static byte[] ReadStream(byte[] data, string streamName, long maxStreamBytes)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxStreamBytes);

        if (data.Length < HeaderBytes)
        {
            throw new WordPerfectDecodeException("the compound document header is truncated");
        }

        var sectorShift = ReadU16(data, 30);
        if (sectorShift is not (9 or 12))
        {
            throw new WordPerfectDecodeException("the compound document declares an unsupported sector size");
        }

        var sectorSize = 1 << sectorShift;
        if (data.Length < 2 * sectorSize)
        {
            throw new WordPerfectDecodeException("the compound document is truncated");
        }

        // The header occupies the first sector even when version 4 uses 4096-byte sectors.
        var sectorCount = (data.Length - sectorSize) / sectorSize;
        var fatSectorCount = ReadU32(data, 44);
        if (fatSectorCount > (uint)sectorCount)
        {
            throw new WordPerfectDecodeException("the compound document declares more FAT sectors than it contains");
        }

        var fat = ReadFat(data, sectorSize, sectorCount, ReadU32(data, 68), ReadU32(data, 72));
        var directory = ReadChain(data, sectorSize, sectorCount, fat, ReadU32(data, 48), "directory");
        var entries = directory.Length / DirectoryEntryBytes;
        if (entries == 0)
        {
            throw new WordPerfectDecodeException("the compound document has no directory entries");
        }

        var streamEntry = FindStream(directory, entries, streamName)
                          ?? throw new WordPerfectDecodeException(
                              $"the compound document does not contain the {streamName} stream");
        var entryOffset = streamEntry * DirectoryEntryBytes;
        var size = ReadU64(directory, entryOffset + 120);
        if (size > (ulong)int.MaxValue || size > (ulong)maxStreamBytes)
        {
            throw new WordPerfectSourceTooLargeException((long)Math.Min(size, long.MaxValue), maxStreamBytes);
        }

        if (size == 0)
        {
            return [];
        }

        var startSector = ReadU32(directory, entryOffset + 116);
        return size < ReadU32(data, 56)
            ? ReadMiniStream(data, sectorSize, sectorCount, fat, directory, startSector, (int)size)
            : ReadRegularStream(data, sectorSize, sectorCount, fat, startSector, (int)size);
    }

    private static List<uint> ReadFat(byte[] data, int sectorSize, int sectorCount, uint firstDifatSector,
        uint difatSectorCount)
    {
        if (difatSectorCount > (uint)sectorCount)
        {
            throw new WordPerfectDecodeException("the compound document declares more DIFAT sectors than it contains");
        }

        var fatSectors = new List<uint>();
        for (var index = 0; index < 109; index++)
        {
            var sector = ReadU32(data, 76 + (index * 4));
            if (sector is not (FreeSector or EndOfChain))
            {
                fatSectors.Add(sector);
            }
        }

        var difatSector = firstDifatSector;
        var visited = new HashSet<uint>();
        for (var index = 0u; index < difatSectorCount; index++)
        {
            if (!IsSector(difatSector, sectorCount) || !visited.Add(difatSector))
            {
                throw new WordPerfectDecodeException("the compound document DIFAT chain is damaged");
            }

            var offset = SectorOffset(difatSector, sectorSize);
            for (var slot = 0; slot < (sectorSize / 4) - 1; slot++)
            {
                var sector = ReadU32(data, offset + (slot * 4));
                if (sector is not (FreeSector or EndOfChain))
                {
                    fatSectors.Add(sector);
                }
            }

            difatSector = ReadU32(data, offset + sectorSize - 4);
        }

        if (fatSectors.Count > sectorCount)
        {
            throw new WordPerfectDecodeException("the compound document FAT sector list is damaged");
        }

        var entriesPerSector = sectorSize / 4;
        var fat = new List<uint>(fatSectors.Count * entriesPerSector);
        foreach (var sector in fatSectors)
        {
            if (!IsSector(sector, sectorCount))
            {
                throw new WordPerfectDecodeException("the compound document FAT sector list is damaged");
            }

            var offset = SectorOffset(sector, sectorSize);
            for (var slot = 0; slot < entriesPerSector; slot++)
            {
                fat.Add(ReadU32(data, offset + (slot * 4)));
            }
        }

        if (fat.Count == 0)
        {
            throw new WordPerfectDecodeException("the compound document has no FAT");
        }

        return fat;
    }

    private static byte[] ReadChain(byte[] data, int sectorSize, int sectorCount, List<uint> fat, uint firstSector,
        string name)
    {
        using var output = new MemoryStream();
        var visited = new HashSet<uint>();
        var sector = firstSector;
        while (sector != EndOfChain)
        {
            WriteSector(data, sectorSize, sectorCount, fat, sector, name, visited, output);
            sector = NextSector(fat, sector, name);
        }

        return output.ToArray();
    }

    private static byte[] ReadRegularStream(byte[] data, int sectorSize, int sectorCount, List<uint> fat,
        uint startSector, int size)
    {
        var result = new byte[size];
        var visited = new HashSet<uint>();
        var sector = startSector;
        var written = 0;
        while (written < size)
        {
            if (!IsSector(sector, sectorCount) || !visited.Add(sector))
            {
                throw new WordPerfectDecodeException("the compound document stream chain is damaged");
            }

            var count = Math.Min(sectorSize, size - written);
            Buffer.BlockCopy(data, SectorOffset(sector, sectorSize), result, written, count);
            written += count;
            sector = NextSector(fat, sector, "stream");
        }

        return result;
    }

    private static byte[] ReadMiniStream(byte[] data, int sectorSize, int sectorCount, List<uint> fat,
        byte[] directory, uint startSector, int size)
    {
        var miniStreamSize = ReadU64(directory, 120);
        if (miniStreamSize == 0 || miniStreamSize > (ulong)data.Length || miniStreamSize > int.MaxValue)
        {
            throw new WordPerfectDecodeException("the compound document mini stream is damaged");
        }

        var miniStream = ReadRegularStream(data, sectorSize, sectorCount, fat, ReadU32(directory, 116),
            (int)miniStreamSize);
        var miniFat = ReadChain(data, sectorSize, sectorCount, fat, ReadU32(data, 60), "mini FAT");
        var miniSectorCount = miniStream.Length / MiniSectorBytes;
        var miniFatEntries = miniFat.Length / 4;

        var result = new byte[size];
        var visited = new HashSet<uint>();
        var sector = startSector;
        var written = 0;
        while (written < size)
        {
            if (sector >= miniSectorCount || sector >= miniFatEntries || !visited.Add(sector))
            {
                throw new WordPerfectDecodeException("the compound document mini stream chain is damaged");
            }

            var count = Math.Min(MiniSectorBytes, size - written);
            Buffer.BlockCopy(miniStream, (int)sector * MiniSectorBytes, result, written, count);
            written += count;
            sector = ReadU32(miniFat, (int)sector * 4);
            if (sector is FreeSector or FatSector or DifatSector)
            {
                throw new WordPerfectDecodeException("the compound document mini stream chain is damaged");
            }
        }

        return result;
    }

    private static int? FindStream(byte[] directory, int entries, string streamName)
    {
        for (var index = 1; index < entries; index++)
        {
            var offset = index * DirectoryEntryBytes;
            if (directory[offset + 66] != 2)
            {
                continue;
            }

            var nameBytes = ReadU16(directory, offset + 64);
            if (nameBytes is < 2 or > 64 || (nameBytes & 1) != 0)
            {
                continue;
            }

            var name = Encoding.Unicode.GetString(directory, offset, nameBytes - 2);
            if (string.Equals(name, streamName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return null;
    }

    private static void WriteSector(byte[] data, int sectorSize, int sectorCount, List<uint> fat, uint sector,
        string name, HashSet<uint> visited, Stream output)
    {
        if (!IsSector(sector, sectorCount) || !visited.Add(sector))
        {
            throw new WordPerfectDecodeException($"the compound document {name} chain is damaged");
        }

        output.Write(data.AsSpan(SectorOffset(sector, sectorSize), sectorSize));
    }

    private static uint NextSector(List<uint> fat, uint sector, string name)
    {
        if (sector >= fat.Count)
        {
            throw new WordPerfectDecodeException($"the compound document {name} chain is truncated");
        }

        var next = fat[(int)sector];
        if (next is FreeSector or FatSector or DifatSector)
        {
            throw new WordPerfectDecodeException($"the compound document {name} chain is damaged");
        }

        return next;
    }

    private static bool IsSector(uint sector, int sectorCount) => sector < (uint)sectorCount;

    private static int SectorOffset(uint sector, int sectorSize) =>
        checked((int)(((long)sector + 1) * sectorSize));

    private static ushort ReadU16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadU32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static ulong ReadU64(byte[] data, int offset) =>
        ReadU32(data, offset) | ((ulong)ReadU32(data, offset + 4) << 32);
}
