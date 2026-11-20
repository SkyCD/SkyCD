using System;
using System.Collections.Generic;
using System.Text;

namespace SkyCD.Plugin.Works.Tests;

/// <summary>
/// A property-set value as it appears on disk: identifier, variant type tag and the encoded value bytes.
/// </summary>
internal sealed record SummaryProperty(uint Id, uint Type, byte[] Payload);

/// <summary>
/// A named stream and its contents inside a fixture compound file.
/// </summary>
internal sealed record CompoundStream(string Name, byte[] Data);

/// <summary>
/// Builds structurally valid Microsoft Works documents and OLE2 compound wrappers in memory, so the suite
/// commits no binary blobs and every expected value is known to the test asserting it.
/// </summary>
internal static class WorksFixtures
{
    public const string Mn0Stream = "MN0";
    public const string ContentsStream = "CONTENTS";
    public const string SummaryStream = "\u0005SummaryInformation";
    public const string DocumentSummaryStream = "\u0005DocumentSummaryInformation";

    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int MiniStreamCutoff = 4096;
    private const int FlatLength = 0x200;

    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSectorMarker = 0xFFFFFFFD;

    private static readonly byte[] CompoundSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    static WorksFixtures()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Works 2 and early Works 3 stored the document flat, starting with the version and 0xFE.</summary>
    public static byte[] Flat(byte version = 2)
    {
        var data = new byte[FlatLength];
        data[0] = version;
        data[1] = 0xFE;
        return data;
    }

    /// <summary>A Works 3 compound document whose MN0 stream starts with the expected header.</summary>
    public static byte[] Works3(params SummaryProperty[] summary) =>
        Works3(summary, []);

    public static byte[] Works3(SummaryProperty[] summary, SummaryProperty[] documentSummary) =>
        CompoundWorks(Mn0Header(0x04), summary, documentSummary);

    /// <summary>A Works 4 compound document, identified by a header version of 6 or more.</summary>
    public static byte[] Works4(SummaryProperty[] summary, SummaryProperty[] documentSummary) =>
        CompoundWorks(Mn0Header(0x06), summary, documentSummary);

    /// <summary>Works 2000 identifies itself through the CONTENTS chunk header.</summary>
    public static byte[] Works2000(params SummaryProperty[] summary) =>
        CompoundWorks(ContentsHeader("CHNKINK"), summary, []);

    /// <summary>Works 7 and 8 identify themselves through the CONTENTS chunk header.</summary>
    public static byte[] Works7(params SummaryProperty[] summary) =>
        CompoundWorks(ContentsHeader("CHNKWKS"), summary, []);

    /// <summary>
    /// A Works 4 document whose MN0 stream is larger than the mini-stream cutoff, so the reader takes the
    /// regular-sector path instead of the mini-stream path.
    /// </summary>
    public static byte[] Works4WithLargeHeader(params SummaryProperty[] summary) =>
        CompoundWorks(Mn0Header(0x06, length: 5000), summary, []);

    /// <summary>A compound file with the given streams and no Works document stream at all.</summary>
    public static byte[] CompoundWithoutWorksStream(params CompoundStream[] streams) => Compound(streams);

    /// <summary>A compound file whose summary stream is present but is not a property set stream.</summary>
    public static byte[] Works3WithDamagedSummary() =>
        CompoundWorks(Mn0Header(0x04), [], [],
            new CompoundStream(SummaryStream, new byte[64]));

    /// <summary>A compound file whose MN0 stream does not carry the Works header bytes.</summary>
    public static byte[] Works3WithDamagedHeader() =>
        Compound([new CompoundStream(Mn0Stream, Mn0Header(0x04, broken: true))]);

    public static byte[] NotWorks()
    {
        var data = new byte[FlatLength];
        Encoding.ASCII.GetBytes("This is not a Microsoft Works document.").CopyTo(data, 0);
        return data;
    }

    public static byte[] Truncate(byte[] data, int bytes) => data.AsSpan(0, data.Length - bytes).ToArray();

    /// <summary>A complete Works 4 summary that populates every published field.</summary>
    public static SummaryProperty[] FullSummary() =>
    [
        Codepage(),
        Text(2, "Quarterly Report"),
        Text(3, "Figures"),
        Text(4, "A. Writer"),
        Text(5, "finance; report"),
        Text(6, "Reviewed once"),
        Text(7, "Normal.dot"),
        Text(8, "Editor"),
        Text(9, "3"),
        Text(18, "Microsoft Works"),
        FileTime(11, LastPrinted),
        FileTime(12, Created),
        FileTime(13, Modified),
        Int4(14, 5),
        Int4(15, 1234),
        Int4(16, 6789)
    ];

    /// <summary>A complete document summary that populates the published document-level fields.</summary>
    public static SummaryProperty[] FullDocumentSummary() =>
    [
        Codepage(),
        Text(2, "Reports"),
        Text(14, "M. Manager"),
        Text(15, "SkyCD Ltd"),
        Text(27, "Final"),
        Text(28, "en-GB")
    ];

    public static readonly DateTimeOffset Created = new(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
    public static readonly DateTimeOffset Modified = new(2021, 6, 7, 8, 9, 10, TimeSpan.Zero);
    public static readonly DateTimeOffset LastPrinted = new(2019, 12, 31, 23, 59, 58, TimeSpan.Zero);

    public static SummaryProperty Codepage(int codePage = 1252) =>
        new(1, 0x0002, [(byte)codePage, (byte)(codePage >> 8), 0, 0]);

    public static SummaryProperty Int2(uint id, short value) =>
        new(id, 0x0002, [(byte)value, (byte)(value >> 8), 0, 0]);

    public static SummaryProperty Int4(uint id, int value) =>
        new(id, 0x0003, [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)]);

    /// <summary>VT_LPWSTR, the wide string variant used by most summary writers.</summary>
    public static SummaryProperty Text(uint id, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var payload = new byte[4 + bytes.Length + 2];
        WriteU32(payload, 0, (uint)value.Length);
        bytes.CopyTo(payload, 4);
        return new SummaryProperty(id, 0x001F, payload);
    }

    /// <summary>VT_LPSTR, whose bytes are interpreted through the stream's codepage property.</summary>
    public static SummaryProperty AnsiText(uint id, string value, int codePage = 1252)
    {
        var bytes = Encoding.GetEncoding(codePage).GetBytes(value);
        var payload = new byte[4 + bytes.Length + 1];
        WriteU32(payload, 0, (uint)bytes.Length);
        bytes.CopyTo(payload, 4);
        return new SummaryProperty(id, 0x001E, payload);
    }

    public static SummaryProperty FileTime(uint id, DateTimeOffset value)
    {
        var payload = new byte[8];
        WriteU64(payload, 0, (ulong)value.UtcDateTime.ToFileTimeUtc());
        return new SummaryProperty(id, 0x0040, payload);
    }

    /// <summary>Encodes a one-section OLE property set stream as <c>SummaryInformation</c> stores it.</summary>
    public static byte[] PropertySet(params SummaryProperty[] properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        const int sectionOffset = 48;
        var tableSize = 8 * properties.Length;
        var offsets = new int[properties.Length];
        var position = 8 + tableSize;
        for (var index = 0; index < properties.Length; index++)
        {
            position = Align4(position);
            offsets[index] = position;
            position += 4 + Align4(properties[index].Payload.Length);
        }

        var sectionSize = Align4(position);
        var stream = new byte[sectionOffset + sectionSize];
        WriteU16(stream, 0, 0xFFFE);
        WriteU32(stream, 24, 1);

        // A recognizable FMTID is unnecessary: the reader keys values by property id only.
        WriteU32(stream, 28, 0xE0859FF2);
        WriteU32(stream, 32, 0x10684FF9);
        WriteU32(stream, 36, 0x2B080091);
        WriteU32(stream, 40, 0xD927B302);
        WriteU32(stream, 44, sectionOffset);

        WriteU32(stream, sectionOffset, (uint)sectionSize);
        WriteU32(stream, sectionOffset + 4, (uint)properties.Length);

        var table = sectionOffset + 8;
        for (var index = 0; index < properties.Length; index++)
        {
            WriteU32(stream, table + (index * 8), properties[index].Id);
            WriteU32(stream, table + (index * 8) + 4, (uint)offsets[index]);

            var value = sectionOffset + offsets[index];
            WriteU32(stream, value, properties[index].Type);
            properties[index].Payload.CopyTo(stream, value + 4);
        }

        return stream;
    }

    private static byte[] Mn0Header(byte version, bool broken = false, int length = FlatLength)
    {
        var data = new byte[length];
        data[0] = broken ? (byte)0xAA : version;
        data[1] = 0xFE;
        return data;
    }

    private static byte[] ContentsHeader(string magic, int length = FlatLength)
    {
        var data = new byte[length];
        Encoding.ASCII.GetBytes(magic).CopyTo(data, 0);
        return data;
    }

    private static byte[] CompoundWorks(byte[] worksStream, SummaryProperty[] summary,
        SummaryProperty[] documentSummary, params CompoundStream[] extra)
    {
        // CONTENTS-based documents share the "CHNK" prefix; everything else is an MN0 stream.
        var worksStreamName = worksStream.Length >= 4 && worksStream[0] == (byte)'C'
            ? ContentsStream
            : Mn0Stream;

        var streams = new List<CompoundStream> { new(worksStreamName, worksStream) };

        if (summary.Length > 0)
        {
            streams.Add(new CompoundStream(SummaryStream, PropertySet(summary)));
        }

        if (documentSummary.Length > 0)
        {
            streams.Add(new CompoundStream(DocumentSummaryStream, PropertySet(documentSummary)));
        }

        streams.AddRange(extra);
        return Compound([.. streams]);
    }

    /// <summary>
    /// Writes a compound file with one FAT sector, one directory sector and both regular and mini streams.
    /// The builder supports at most four directory entries (root plus three streams).
    /// </summary>
    private static byte[] Compound(CompoundStream[] streams)
    {
        if (streams.Length > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(streams), "The fixture keeps every directory entry in one sector.");
        }

        const int fatSector = 0;
        const int directorySector = 1;
        var next = 2;

        var regularStart = new Dictionary<string, int>(StringComparer.Ordinal);
        var regularSectors = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var stream in streams)
        {
            if (stream.Data.Length < MiniStreamCutoff)
            {
                continue;
            }

            var sectors = DivideRoundUp(stream.Data.Length, SectorSize);
            regularStart[stream.Name] = next;
            regularSectors[stream.Name] = sectors;
            next += sectors;
        }

        var miniStream = new List<byte>();
        var miniFat = new List<uint>();
        var miniStart = new Dictionary<string, int>(StringComparer.Ordinal);
        var miniSectors = new Dictionary<string, int>(StringComparer.Ordinal);
        var hasMini = false;
        foreach (var stream in streams)
        {
            if (stream.Data.Length >= MiniStreamCutoff)
            {
                continue;
            }

            hasMini = true;
            var count = DivideRoundUp(stream.Data.Length, MiniSectorSize);
            miniStart[stream.Name] = miniFat.Count;
            miniSectors[stream.Name] = count;

            var padded = new byte[count * MiniSectorSize];
            stream.Data.CopyTo(padded, 0);
            miniStream.AddRange(padded);
            for (var index = 0; index < count; index++)
            {
                miniFat.Add(index == count - 1 ? EndOfChain : (uint)(miniFat.Count + 1));
            }
        }

        var miniContainerStart = next;
        var miniContainerSectors = hasMini ? DivideRoundUp(miniStream.Count, SectorSize) : 0;
        next += miniContainerSectors;
        var miniFatSector = hasMini ? (uint)next++ : EndOfChain;

        var totalSectors = next;
        if (totalSectors > SectorSize / 4)
        {
            throw new ArgumentOutOfRangeException(nameof(streams), "The fixture fits one FAT sector.");
        }

        var data = new byte[SectorSize + (totalSectors * SectorSize)];
        WriteHeader(data, directorySector, miniFatSector, hasMini ? 1u : 0u);

        var fatOffset = SectorOffset(fatSector);
        for (var entry = 0; entry < SectorSize / 4; entry++)
        {
            WriteU32(data, fatOffset + (entry * 4), FreeSector);
        }

        WriteU32(data, fatOffset, FatSectorMarker);
        WriteU32(data, fatOffset + 4, EndOfChain);

        var directoryOffset = SectorOffset(directorySector);
        WriteDirectoryEntry(data, directoryOffset, "Root Entry", 5, FreeSector,
            hasMini ? (uint)miniContainerStart : EndOfChain, (ulong)miniStream.Count);

        for (var index = 0; index < streams.Length; index++)
        {
            var stream = streams[index];
            var isRegular = stream.Data.Length >= MiniStreamCutoff;
            var start = isRegular ? regularStart[stream.Name] : miniStart[stream.Name];
            var sectors = isRegular ? regularSectors[stream.Name] : miniSectors[stream.Name];
            WriteDirectoryEntry(data, directoryOffset + (128 * (index + 1)), stream.Name, 2,
                FreeSector, (uint)start, (ulong)stream.Data.Length);

            if (!isRegular)
            {
                continue;
            }

            for (var sectorIndex = 0; sectorIndex < sectors; sectorIndex++)
            {
                WriteU32(data, fatOffset + ((start + sectorIndex) * 4),
                    sectorIndex == sectors - 1 ? EndOfChain : (uint)(start + sectorIndex + 1));
            }

            stream.Data.CopyTo(data, SectorOffset(start));
        }

        if (hasMini)
        {
            for (var sectorIndex = 0; sectorIndex < miniContainerSectors; sectorIndex++)
            {
                WriteU32(data, fatOffset + ((miniContainerStart + sectorIndex) * 4),
                    sectorIndex == miniContainerSectors - 1
                        ? EndOfChain
                        : (uint)(miniContainerStart + sectorIndex + 1));
            }

            WriteU32(data, fatOffset + ((int)miniFatSector * 4), EndOfChain);
            miniStream.CopyTo(data, SectorOffset(miniContainerStart));

            var miniFatOffset = SectorOffset((int)miniFatSector);
            for (var entry = 0; entry < SectorSize / 4; entry++)
            {
                WriteU32(data, miniFatOffset + (entry * 4), FreeSector);
            }

            for (var index = 0; index < miniFat.Count; index++)
            {
                WriteU32(data, miniFatOffset + (index * 4), miniFat[index]);
            }
        }

        return data;
    }

    private static void WriteHeader(byte[] data, int firstDirectorySector, uint firstMiniFatSector,
        uint miniFatSectorCount)
    {
        CompoundSignature.CopyTo(data, 0);
        WriteU16(data, 24, 0x003E);
        WriteU16(data, 26, 3);
        WriteU16(data, 28, 0xFFFE);
        WriteU16(data, 30, 9);
        WriteU16(data, 32, 6);
        WriteU32(data, 40, 0);
        WriteU32(data, 44, 1);
        WriteU32(data, 48, (uint)firstDirectorySector);
        WriteU32(data, 56, MiniStreamCutoff);
        WriteU32(data, 60, firstMiniFatSector);
        WriteU32(data, 64, miniFatSectorCount);
        WriteU32(data, 68, EndOfChain);
        WriteU32(data, 72, 0);

        for (var slot = 0; slot < 109; slot++)
        {
            WriteU32(data, 76 + (slot * 4), FreeSector);
        }

        WriteU32(data, 76, 0);
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

    private static int Align4(int value) => (value + 3) & ~3;

    private static int DivideRoundUp(int value, int unit) => (value + unit - 1) / unit;

    private static int SectorOffset(int sector) =>
        checked(SectorSize + (sector * SectorSize));

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
