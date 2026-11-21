using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.Works.Exceptions;

namespace SkyCD.Plugin.Works.Parsing;

/// <summary>
/// Read-only access to named streams in an OLE2 compound file held in memory.
/// </summary>
/// <remarks>
/// Microsoft Works 3 and later wrap the document in a compound file. This reader exists instead of a package
/// dependency because the established .NET compound-file library is MPL-2.0, which the repository's
/// BSD-2-Clause outbound policy does not accept (<c>docs/legal/osadl-bsd-2-clause.json</c>). Only the read
/// path is implemented: every declared sector, stream size and chain link is checked against the bytes
/// actually present, and traversal is bounded by the sector count, so a damaged header cannot cause an
/// unbounded walk or allocation.
/// </remarks>
internal sealed class CompoundFile
{
    private const int HeaderBytes = 512;
    private const int DirectoryEntryBytes = 128;
    private const int MiniSectorBytes = 64;
    private const int DirectoryEntryStream = 2;
    private const int DirectoryEntryRoot = 5;

    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint DifatSector = 0xFFFFFFFC;

    private static readonly byte[] Signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private readonly byte[] data;
    private readonly int sectorSize;
    private readonly int sectorCount;
    private readonly uint miniStreamCutoff;
    private readonly List<uint> fat;
    private readonly byte[] directory;
    private readonly int directoryEntryCount;

    private byte[]? miniStream;
    private byte[]? miniFat;

    private CompoundFile(byte[] data, int sectorSize, int sectorCount, uint miniStreamCutoff,
        List<uint> fat, byte[] directory)
    {
        this.data = data;
        this.sectorSize = sectorSize;
        this.sectorCount = sectorCount;
        this.miniStreamCutoff = miniStreamCutoff;
        this.fat = fat;
        this.directory = directory;
        directoryEntryCount = directory.Length / DirectoryEntryBytes;
    }

    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= Signature.Length && data[..Signature.Length].SequenceEqual(Signature);

    public static CompoundFile Open(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!HasSignature(data))
        {
            throw new WorksDecodeException("the source is not a compound file");
        }

        if (data.Length < HeaderBytes)
        {
            throw new WorksDecodeException("the compound document header is truncated");
        }

        var sectorShift = ReadU16(data, 30);
        if (sectorShift is not (9 or 12))
        {
            throw new WorksDecodeException("the compound document declares an unsupported sector size");
        }

        var sectorSize = 1 << sectorShift;
        if (data.Length < 2 * sectorSize)
        {
            throw new WorksDecodeException("the compound document is truncated");
        }

        // The header occupies the first sector even when version 4 uses 4096-byte sectors.
        var sectorCount = (data.Length - sectorSize) / sectorSize;
        var fatSectorCount = ReadU32(data, 44);
        if (fatSectorCount > (uint)sectorCount)
        {
            throw new WorksDecodeException("the compound document declares more FAT sectors than it contains");
        }

        var fat = ReadFat(data, sectorSize, sectorCount, ReadU32(data, 68), ReadU32(data, 72));
        var directory = ReadChain(data, sectorSize, sectorCount, fat, ReadU32(data, 48), "directory");
        if (directory.Length / DirectoryEntryBytes == 0)
        {
            throw new WorksDecodeException("the compound document has no directory entries");
        }

        var cutoff = ReadU32(data, 56);
        return new CompoundFile(data, sectorSize, sectorCount, cutoff == 0 ? 4096u : cutoff, fat, directory);
    }

    /// <summary>Reads a whole stream, or returns <see langword="false"/> when it is missing or too large.</summary>
    public bool TryReadStream(string name, long maxBytes, out byte[] stream)
    {
        stream = [];
        var entry = FindEntry(name);
        if (entry is null || entry.Value.Size == 0 || entry.Value.Size > (ulong)maxBytes
            || entry.Value.Size > int.MaxValue)
        {
            return false;
        }

        stream = ReadStream(entry.Value, (int)entry.Value.Size);
        return true;
    }

    /// <summary>
    /// Reads the leading bytes of a stream. The full declared length is reported through
    /// <paramref name="length"/> so callers can validate a header without materializing a large stream.
    /// </summary>
    public bool TryReadStreamPrefix(string name, int count, out byte[] prefix, out long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        prefix = [];
        length = 0;
        var entry = FindEntry(name);
        if (entry is null)
        {
            return false;
        }

        length = (long)Math.Min(entry.Value.Size, long.MaxValue);
        var take = (int)Math.Min(entry.Value.Size, (ulong)count);
        if (take == 0)
        {
            return true;
        }

        prefix = ReadStream(entry.Value, take);
        return true;
    }

    private byte[] ReadStream(DirectoryEntry entry, int count)
    {
        // The storage kind follows the stream's declared size, not how many bytes the caller wants.
        if (entry.Size >= miniStreamCutoff)
        {
            return ReadRegularStream(entry.StartSector, count);
        }

        return ReadMiniStream(entry.StartSector, count);
    }

    private byte[] ReadRegularStream(uint startSector, int size)
    {
        var result = new byte[size];
        var visited = new HashSet<uint>();
        var sector = startSector;
        var written = 0;
        while (written < size)
        {
            if (!IsSector(sector) || !visited.Add(sector))
            {
                throw new WorksDecodeException("the compound document stream chain is damaged");
            }

            var available = Math.Min(sectorSize, size - written);
            Buffer.BlockCopy(data, SectorOffset(sector), result, written, available);
            written += available;
            sector = NextSector(sector, "stream");
        }

        return result;
    }

    private byte[] ReadMiniStream(uint startMiniSector, int size)
    {
        var stream = GetMiniStream();
        var miniFatBytes = GetMiniFat();
        var miniSectorCount = stream.Length / MiniSectorBytes;
        var miniFatEntries = miniFatBytes.Length / 4;

        var result = new byte[size];
        var visited = new HashSet<uint>();
        var sector = startMiniSector;
        var written = 0;
        while (written < size)
        {
            if (sector >= (uint)miniSectorCount || sector >= (uint)miniFatEntries || !visited.Add(sector))
            {
                throw new WorksDecodeException("the compound document mini stream chain is damaged");
            }

            var available = Math.Min(MiniSectorBytes, size - written);
            Buffer.BlockCopy(stream, (int)sector * MiniSectorBytes, result, written, available);
            written += available;

            sector = ReadU32(miniFatBytes, (int)sector * 4);
            if (sector is FreeSector or FatSector or DifatSector)
            {
                throw new WorksDecodeException("the compound document mini stream chain is damaged");
            }
        }

        return result;
    }

    private byte[] GetMiniStream()
    {
        if (miniStream is not null)
        {
            return miniStream;
        }

        if (directoryEntryCount == 0 || directory[66] != DirectoryEntryRoot)
        {
            throw new WorksDecodeException("the compound document has no root entry");
        }

        var size = ReadU64(directory, 120);
        if (size == 0 || size > (ulong)data.Length || size > int.MaxValue)
        {
            throw new WorksDecodeException("the compound document mini stream is damaged");
        }

        miniStream = ReadRegularStream(ReadU32(directory, 116), (int)size);
        return miniStream;
    }

    private byte[] GetMiniFat()
    {
        if (miniFat is not null)
        {
            return miniFat;
        }

        miniFat = ReadChain(data, sectorSize, sectorCount, fat, ReadU32(data, 60), "mini FAT");
        if (miniFat.Length == 0)
        {
            throw new WorksDecodeException("the compound document has no mini FAT");
        }

        return miniFat;
    }

    private DirectoryEntry? FindEntry(string name)
    {
        for (var index = 0; index < directoryEntryCount; index++)
        {
            var offset = index * DirectoryEntryBytes;
            if (directory[offset + 66] != DirectoryEntryStream)
            {
                continue;
            }

            var nameBytes = ReadU16(directory, offset + 64);
            if (nameBytes is < 2 or > 64 || (nameBytes & 1) != 0)
            {
                continue;
            }

            var candidate = Encoding.Unicode.GetString(directory, offset, nameBytes - 2);
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return new DirectoryEntry(ReadU32(directory, offset + 116), ReadU64(directory, offset + 120));
            }
        }

        return null;
    }

    private static List<uint> ReadFat(byte[] data, int sectorSize, int sectorCount, uint firstDifatSector,
        uint difatSectorCount)
    {
        if (difatSectorCount > (uint)sectorCount)
        {
            throw new WorksDecodeException("the compound document declares more DIFAT sectors than it contains");
        }

        var fatSectors = new List<uint>();
        for (var index = 0; index < 109; index++)
        {
            var sector = ReadU32(data, 76 + (index * 4));
            if (sector is not (FreeSector or EndOfChain or DifatSector))
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
                throw new WorksDecodeException("the compound document DIFAT chain is damaged");
            }

            var offset = SectorOffset(difatSector, sectorSize);
            for (var slot = 0; slot < (sectorSize / 4) - 1; slot++)
            {
                var sector = ReadU32(data, offset + (slot * 4));
                if (sector is not (FreeSector or EndOfChain or DifatSector))
                {
                    fatSectors.Add(sector);
                }
            }

            difatSector = ReadU32(data, offset + sectorSize - 4);
        }

        if (fatSectors.Count == 0 || fatSectors.Count > sectorCount)
        {
            throw new WorksDecodeException("the compound document FAT sector list is damaged");
        }

        var entriesPerSector = sectorSize / 4;
        var fat = new List<uint>(fatSectors.Count * entriesPerSector);
        foreach (var sector in fatSectors)
        {
            if (!IsSector(sector, sectorCount))
            {
                throw new WorksDecodeException("the compound document FAT sector list is damaged");
            }

            var offset = SectorOffset(sector, sectorSize);
            for (var slot = 0; slot < entriesPerSector; slot++)
            {
                fat.Add(ReadU32(data, offset + (slot * 4)));
            }
        }

        return fat;
    }

    private static byte[] ReadChain(byte[] data, int sectorSize, int sectorCount, List<uint> fat,
        uint firstSector, string name)
    {
        using var output = new MemoryStream();
        var visited = new HashSet<uint>();
        var sector = firstSector;
        while (sector != EndOfChain)
        {
            if (!IsSector(sector, sectorCount) || !visited.Add(sector))
            {
                throw new WorksDecodeException($"the compound document {name} chain is damaged");
            }

            output.Write(data.AsSpan(SectorOffset(sector, sectorSize), sectorSize));
            sector = NextSector(fat, sector, name);
        }

        return output.ToArray();
    }

    private uint NextSector(uint sector, string name) => NextSector(fat, sector, name);

    private static uint NextSector(List<uint> fat, uint sector, string name)
    {
        if (sector >= (uint)fat.Count)
        {
            throw new WorksDecodeException($"the compound document {name} chain is truncated");
        }

        var next = fat[(int)sector];
        if (next is FreeSector or FatSector or DifatSector)
        {
            throw new WorksDecodeException($"the compound document {name} chain is damaged");
        }

        return next;
    }

    private bool IsSector(uint sector) => sector < (uint)sectorCount;

    private static bool IsSector(uint sector, int sectorCount) => sector < (uint)sectorCount;

    private int SectorOffset(uint sector) => SectorOffset(sector, sectorSize);

    private static int SectorOffset(uint sector, int sectorSize) =>
        checked((int)(((long)sector + 1) * sectorSize));

    private static ushort ReadU16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadU32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static ulong ReadU64(byte[] data, int offset) =>
        ReadU32(data, offset) | ((ulong)ReadU32(data, offset + 4) << 32);

    private readonly record struct DirectoryEntry(uint StartSector, ulong Size);
}
