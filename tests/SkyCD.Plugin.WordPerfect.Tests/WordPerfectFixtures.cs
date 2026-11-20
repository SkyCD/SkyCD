using System;
using System.Text;

namespace SkyCD.Plugin.WordPerfect.Tests;

/// <summary>
/// Builds structurally valid WordPerfect documents and compound-file wrappers in memory, so the suite commits
/// no binary blobs and every expected value is known to the test asserting it.
/// </summary>
internal static class WordPerfectFixtures
{
    public const int IndexHeaderOffset = 512;
    public const int SummaryOffset = 1024;

    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSectorMarker = 0xFFFFFFFD;

    private static readonly byte[] CompoundSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public static byte[] Document(params byte[][] groups) => WithSummary(Summary(groups));

    public static byte[] WithoutSummary() => WithSummary([], includeSummary: false);

    public static byte[] WithSummary(byte[] summary, byte fileType = 0x0A, byte majorVersion = 2,
        byte minorVersion = 1, ushort encryption = 0, bool includeSummary = true)
    {
        var length = Math.Max(SummaryOffset, SummaryOffset + summary.Length);
        var data = new byte[length];
        data[0] = 0xFF;
        data[1] = (byte)'W';
        data[2] = (byte)'P';
        data[3] = (byte)'C';
        WriteU32(data, 4, 16);
        data[8] = 1;
        data[9] = fileType;
        data[10] = majorVersion;
        data[11] = minorVersion;
        WriteU16(data, 12, encryption);
        WriteU16(data, 14, IndexHeaderOffset);

        data[IndexHeaderOffset] = 2;
        WriteU16(data, IndexHeaderOffset + 2, (ushort)(includeSummary ? 2 : 1));
        if (includeSummary)
        {
            var entry = IndexHeaderOffset + 14;
            data[entry] = 0;
            data[entry + 1] = 0x12;
            WriteU16(data, entry + 2, 1);
            WriteU16(data, entry + 4, 0);
            WriteU32(data, entry + 6, (uint)summary.Length);
            WriteU32(data, entry + 10, SummaryOffset);
            summary.CopyTo(data, SummaryOffset);
        }

        return data;
    }

    public static byte[] Summary(params byte[][] groups)
    {
        var length = 0;
        foreach (var group in groups)
        {
            length += group.Length;
        }

        var data = new byte[length];
        var offset = 0;
        foreach (var group in groups)
        {
            group.CopyTo(data, offset);
            offset += group.Length;
        }

        return data;
    }

    public static byte[] TextGroup(ushort tag, string? name, string value, byte characterSet = 0) =>
        Group(tag, WordString(name, characterSet), WordString(value, characterSet));

    public static byte[] DateGroup(ushort tag, ushort year, byte month, byte day, byte hour = 12,
        byte minute = 30, byte second = 15, string? name = null) =>
        Group(tag, WordString(name, 0), DateValue(year, month, day, hour, minute, second));

    public static byte[] EmptyGroup() => [0, 0];

    public static byte[] WithDeclaredLength(byte[] group, ushort declaredLength)
    {
        var copy = (byte[])group.Clone();
        WriteU16(copy, 0, declaredLength);
        return copy;
    }

    public static byte[] WrapInCompoundDocument(byte[] document, string streamName = "PerfectOffice_MAIN")
    {
        var streamLength = Math.Max(4096, RoundUp(document.Length, 512));
        var streamSectors = streamLength / 512;
        const int fatSector = 0;
        const int directorySector = 1;
        const int firstStreamSector = 2;
        var totalSectors = firstStreamSector + streamSectors;
        var data = new byte[512 + (totalSectors * 512)];

        WriteCompoundHeader(data, [fatSector], directorySector, EndOfChain, 0, EndOfChain, 0);

        var fatOffset = SectorOffset(fatSector);
        for (var entry = 0; entry < 128; entry++)
        {
            WriteU32(data, fatOffset + (entry * 4), FreeSector);
        }

        WriteU32(data, fatOffset, FatSectorMarker);
        WriteU32(data, fatOffset + 4, EndOfChain);
        for (var index = 0; index < streamSectors; index++)
        {
            WriteU32(data, fatOffset + ((firstStreamSector + index) * 4),
                index == streamSectors - 1 ? EndOfChain : (uint)(firstStreamSector + index + 1));
        }

        var directoryOffset = SectorOffset(directorySector);
        WriteDirectoryEntry(data, directoryOffset, "Root Entry", 5, 1, EndOfChain, 0);
        WriteDirectoryEntry(data, directoryOffset + 128, streamName, 2, FreeSector, firstStreamSector,
            (ulong)streamLength);
        document.CopyTo(data, SectorOffset(firstStreamSector));
        return data;
    }

    public static byte[] WrapInCompoundDocumentUsingMiniStream(byte[] document)
    {
        var miniStreamLength = RoundUp(document.Length, 64);
        var miniStreamSectors = RoundUp(miniStreamLength, 512) / 512;
        var miniFatSector = 2 + miniStreamSectors;
        var totalSectors = miniFatSector + 1;
        var data = new byte[512 + (totalSectors * 512)];

        WriteCompoundHeader(data, [0], 1, (uint)miniFatSector, 1, EndOfChain, 0);

        var fatOffset = SectorOffset(0);
        for (var entry = 0; entry < 128; entry++)
        {
            WriteU32(data, fatOffset + (entry * 4), FreeSector);
        }

        WriteU32(data, fatOffset, FatSectorMarker);
        WriteU32(data, fatOffset + 4, EndOfChain);
        for (var index = 0; index < miniStreamSectors; index++)
        {
            WriteU32(data, fatOffset + ((2 + index) * 4),
                index == miniStreamSectors - 1 ? EndOfChain : (uint)(2 + index + 1));
        }

        WriteU32(data, fatOffset + (miniFatSector * 4), EndOfChain);

        document.CopyTo(data, SectorOffset(2));

        var miniFatOffset = SectorOffset(miniFatSector);
        for (var entry = 0; entry < 128; entry++)
        {
            WriteU32(data, miniFatOffset + (entry * 4), FreeSector);
        }

        var miniSectorCount = miniStreamLength / 64;
        for (var index = 0; index < miniSectorCount; index++)
        {
            WriteU32(data, miniFatOffset + (index * 4),
                index == miniSectorCount - 1 ? EndOfChain : (uint)(index + 1));
        }

        var directoryOffset = SectorOffset(1);
        WriteDirectoryEntry(data, directoryOffset, "Root Entry", 5, 1, 2, (ulong)miniStreamLength);
        WriteDirectoryEntry(data, directoryOffset + 128, "PerfectOffice_MAIN", 2, FreeSector, 0,
            (ulong)document.Length);
        return data;
    }

    public static byte[] Truncate(byte[] data, int bytes) => data.AsSpan(0, data.Length - bytes).ToArray();

    private static byte[] Group(ushort tag, byte[] name, byte[] value)
    {
        var body = new byte[4 + name.Length + value.Length];
        WriteU16(body, 0, tag);
        WriteU16(body, 2, 0);
        name.CopyTo(body, 4);
        value.CopyTo(body, 4 + name.Length);

        var group = new byte[2 + body.Length];
        WriteU16(group, 0, checked((ushort)group.Length));
        body.CopyTo(group, 2);
        return group;
    }

    private static byte[] WordString(string? value, byte characterSet)
    {
        var characters = string.IsNullOrEmpty(value) ? 0 : value.Length;
        var data = new byte[(characters + 1) * 2];
        for (var index = 0; index < characters; index++)
        {
            var character = value![index];
            if (character > 0xFF)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Fixtures only encode single-byte characters.");
            }

            WriteU16(data, index * 2, (ushort)((characterSet << 8) | character));
        }

        return data;
    }

    private static byte[] DateValue(ushort year, byte month, byte day, byte hour, byte minute, byte second)
    {
        var data = new byte[10];
        WriteU16(data, 0, year);
        data[2] = month;
        data[3] = day;
        data[4] = hour;
        data[5] = minute;
        data[6] = second;
        return data;
    }

    private static void WriteCompoundHeader(byte[] data, uint[] fatSectors, uint firstDirectorySector,
        uint firstMiniFatSector, uint miniFatSectorCount, uint firstDifatSector, uint difatSectorCount)
    {
        CompoundSignature.CopyTo(data, 0);
        WriteU16(data, 24, 0x003E);
        WriteU16(data, 26, 3);
        WriteU16(data, 28, 0xFFFE);
        WriteU16(data, 30, 9);
        WriteU16(data, 32, 6);
        WriteU32(data, 40, 0);
        WriteU32(data, 44, (uint)fatSectors.Length);
        WriteU32(data, 48, firstDirectorySector);
        WriteU32(data, 56, 4096);
        WriteU32(data, 60, firstMiniFatSector);
        WriteU32(data, 64, miniFatSectorCount);
        WriteU32(data, 68, firstDifatSector);
        WriteU32(data, 72, difatSectorCount);

        for (var slot = 0; slot < 109; slot++)
        {
            WriteU32(data, 76 + (slot * 4), FreeSector);
        }

        for (var slot = 0; slot < fatSectors.Length; slot++)
        {
            WriteU32(data, 76 + (slot * 4), fatSectors[slot]);
        }
    }

    private static void WriteDirectoryEntry(byte[] data, int offset, string name, byte type, uint child,
        uint startSector, ulong size)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name);
        if (nameBytes.Length + 2 > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "Compound file names are limited to 31 characters.");
        }

        nameBytes.CopyTo(data, offset);
        WriteU16(data, offset + 64, (ushort)(nameBytes.Length + 2));
        data[offset + 66] = type;
        data[offset + 67] = 1;
        WriteU32(data, offset + 68, FreeSector);
        WriteU32(data, offset + 72, FreeSector);
        WriteU32(data, offset + 76, child);
        WriteU32(data, offset + 116, startSector);
        WriteU64(data, offset + 120, size);
    }

    private static int SectorOffset(int sector) => 512 + (sector * 512);

    private static int RoundUp(int value, int unit) => ((value + unit - 1) / unit) * unit;

    private static void WriteU16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteU64(byte[] data, int offset, ulong value)
    {
        WriteU32(data, offset, (uint)value);
        WriteU32(data, offset + 4, (uint)(value >> 32));
    }
}
