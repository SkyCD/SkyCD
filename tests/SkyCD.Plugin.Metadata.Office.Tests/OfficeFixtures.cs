using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SkyCD.Plugin.Metadata.Office.Tests;

/// <summary>A property-set value as it appears on disk: identifier, variant type tag and the encoded value bytes.</summary>
internal sealed record SummaryProperty(uint Id, uint Type, byte[] Payload);

/// <summary>A named stream and its contents inside a fixture compound file.</summary>
internal sealed record CompoundStream(string Name, byte[] Data);

/// <summary>
/// Builds structurally valid OOXML packages and OLE2 compound documents in memory, so the suite commits no
/// binary blobs and every expected value is known to the test asserting it.
/// </summary>
/// <remarks>
/// The documents a reading library has to open are produced by that library in
/// <see cref="OfficeFixtures.Documents"/>. The packages built here are the deliberately bare ones: a suite that
/// cannot construct an archive the reader must reject cannot test the rejection either.
/// </remarks>
internal static partial class OfficeFixtures
{
    public const string SummaryStream = "\u0005SummaryInformation";
    public const string DocumentSummaryStream = "\u0005DocumentSummaryInformation";
    public const string EncryptionInfoStream = "EncryptionInfo";
    public const string EncryptedPackageStream = "EncryptedPackage";

    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int MiniStreamCutoff = 4096;

    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSectorMarker = 0xFFFFFFFD;

    private static readonly byte[] CompoundSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private const string XmlProlog = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";

    private const string ContentTypesEntry = "[Content_Types].xml";

    /// <summary>The relationship type a package uses to point at its Dublin Core part.</summary>
    public const string CorePropertiesRelationship =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";

    /// <summary>The content type a package declares for its Dublin Core part.</summary>
    public const string CorePropertiesContentType =
        "application/vnd.openxmlformats-package.core-properties+xml";

    /// <summary>The relationship type a package uses to point at its application-properties part.</summary>
    public const string ExtendedPropertiesRelationship =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";

    /// <summary>The content type a package declares for its application-properties part.</summary>
    public const string ExtendedPropertiesContentType =
        "application/vnd.openxmlformats-officedocument.extended-properties+xml";

    public static readonly DateTimeOffset Created = new(2020, 1, 2, 3, 4, 5, TimeSpan.Zero);
    public static readonly DateTimeOffset Modified = new(2021, 6, 7, 8, 9, 10, TimeSpan.Zero);
    public static readonly DateTimeOffset LastPrinted = new(2019, 12, 31, 23, 59, 58, TimeSpan.Zero);

    /// <summary>
    /// A plan schedule floored to the whole minute: Project stores dates in tenths of a minute and MPX in whole
    /// minutes, so a fixture carrying seconds asks the library for an instant the format cannot hold.
    /// </summary>
    public static readonly DateTimeOffset PlanStart = new(2020, 1, 2, 3, 4, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset PlanFinish = new(2021, 6, 7, 8, 9, 0, TimeSpan.Zero);

    static OfficeFixtures()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ---- packages built as raw archives ----

    /// <summary>
    /// An archive in the package layout with only the named parts. It has no relationships, so the reading
    /// library cannot open it and only a suite asserting a rejection can use it.
    /// </summary>
    public static byte[] Package(string? documentPart, string? core, string? app,
        params (string Name, string Content)[] extra)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");

            if (documentPart is not null)
            {
                Add(archive, documentPart, "<root/>");
            }

            if (core is not null)
            {
                Add(archive, "docProps/core.xml", core);
            }

            if (app is not null)
            {
                Add(archive, "docProps/app.xml", app);
            }

            foreach (var (name, content) in extra)
            {
                Add(archive, name, content);
            }
        }

        return stream.ToArray();
    }

    /// <summary>A package that carries metadata parts but no Word, Excel or PowerPoint document part.</summary>
    public static byte[] PackageWithoutDocumentPart(string? core = null, string? app = null) =>
        Package(null, core, app);

    /// <summary>
    /// Writes one metadata part where the package's own root relationship says it lives, adding that
    /// relationship when the producer left the part out. The path matters: a real Word file names the core part
    /// <c>docProps/core.xml</c>, while the reading library writes an opaque name under
    /// <c>package/services/metadata/core-properties</c>, so a fixture that hard-coded a path would rewrite bytes
    /// no reader ever resolves.
    /// </summary>
    public static byte[] SetMetadataPart(
        byte[] package,
        string relationshipType,
        string path,
        string contentType,
        string content)
    {
        using var stream = new MemoryStream();
        stream.Write(package, 0, package.Length);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var index = archive.GetEntry("_rels/.rels")
                ?? throw new InvalidOperationException("the fixture package has no root relationships");

            var relationships = ReadXml(index);
            var declared = relationships.Root!.Elements()
                .FirstOrDefault(element => element.Name.LocalName.Equals("Relationship", StringComparison.Ordinal)
                    && ((string?)element.Attribute("Type"))?.Equals(relationshipType, StringComparison.Ordinal) == true);

            var target = declared is null
                ? path
                : ((string?)declared.Attribute("Target"))
                  ?? throw new InvalidOperationException("the fixture relationship has no target");

            target = target.TrimStart('.', '/').TrimStart('/');

            if (declared is null)
            {
                relationships.Root.Add(new XElement(
                    relationships.Root.Name.Namespace + "Relationship",
                    new XAttribute("Id", "R" + Guid.NewGuid().ToString("N")),
                    new XAttribute("Type", relationshipType),
                    new XAttribute("Target", target)));

                index.Delete();
                WriteEntry(archive, "_rels/.rels", relationships.ToString(SaveOptions.DisableFormatting));
                DeclareContentType(archive, target, contentType);
            }

            archive.GetEntry(target)?.Delete();
            WriteEntry(archive, target, content);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Adds the content-type override a producer writes alongside a part it invents. Without it a package member
    /// named <c>.xml</c> inherits the default type of the document part, and the packaging library refuses to
    /// open the whole document rather than mislabel that one part.
    /// </summary>
    private static void DeclareContentType(ZipArchive archive, string target, string contentType)
    {
        var index = archive.GetEntry(ContentTypesEntry)
            ?? throw new InvalidOperationException("the fixture package has no content types");

        var types = ReadXml(index);
        var partName = "/" + target;
        if (types.Root!.Elements().Any(element => element.Name.LocalName.Equals("Override", StringComparison.Ordinal)
                && (string?)element.Attribute("PartName") == partName))
        {
            return;
        }

        types.Root.Add(new XElement(
            types.Root.Name.Namespace + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));

        index.Delete();
        WriteEntry(archive, ContentTypesEntry, types.ToString(SaveOptions.DisableFormatting));
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        var xml = content.StartsWith("<?xml", StringComparison.Ordinal) ? content : XmlProlog + content;
        var bytes = Encoding.UTF8.GetBytes(xml);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        return XDocument.Load(reader);
    }

    /// <summary>Copies a package and adds one binary member, mirroring how a producer embeds a thumbnail.</summary>
    public static byte[] WithBinaryEntry(byte[] package, string name, byte[] data)
    {
        using var input = new MemoryStream(package, writable: false);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var copy = target.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                using var from = entry.Open();
                using var to = copy.Open();
                from.CopyTo(to);
            }

            var added = target.CreateEntry(name, CompressionLevel.Fastest);
            using var stream = added.Open();
            stream.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>A structurally readable baseline JPEG carrying only an APP0 segment and a frame header.</summary>
    public static byte[] Jpeg(int width, int height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00,
        0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height,
        (byte)(width >> 8), (byte)width,
        0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        0xFF, 0xD9
    ];

    /// <summary>An uncompressed 24-bit device-independent bitmap as a thumbnail clipboard would carry it.</summary>
    public static byte[] Dib(int width, int height)
    {
        var stride = ((width * 3) + 3) & ~3;
        var data = new byte[40 + (stride * height)];
        WriteU32(data, 0, 40);
        WriteU32(data, 4, (uint)width);
        WriteU32(data, 8, (uint)height);
        data[12] = 1;
        data[13] = 0;
        data[14] = 24;
        WriteU32(data, 20, (uint)(stride * height));
        return data;
    }

    /// <summary>A <c>VT_CF</c> summary property holding one clipboard picture.</summary>
    public static SummaryProperty ClipboardThumbnail(uint format, byte[] data)
    {
        var payload = new byte[8 + data.Length];
        WriteU32(payload, 0, (uint)(4 + data.Length));
        WriteU32(payload, 4, format);
        data.CopyTo(payload, 8);
        return new SummaryProperty(17, 0x0047, payload);
    }

    public static string Core(params (string Name, string Value)[] values) =>
        "<cp:coreProperties" +
        " xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\"" +
        " xmlns:dc=\"http://purl.org/dc/elements/1.1/\"" +
        " xmlns:dcterms=\"http://purl.org/dc/terms/\"" +
        " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
        string.Concat(values.Select(value => $"<{value.Name}>{value.Value}</{value.Name}>")) +
        "</cp:coreProperties>";

    public static string App(params (string Name, string Value)[] values) =>
        "<Properties" +
        " xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"" +
        " xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
        string.Concat(values.Select(value => $"<{value.Name}>{value.Value}</{value.Name}>")) +
        "</Properties>";

    /// <summary>A document-level part that populates every published field.</summary>
    public static string FullCore() => Core(
        ("dc:title", "Quarterly Report"),
        ("dc:subject", "Figures"),
        ("dc:creator", "A. Writer"),
        ("cp:keywords", "finance; report"),
        ("dc:description", "Reviewed once"),
        ("cp:category", "Reports"),
        ("cp:contentStatus", "Final"),
        ("cp:lastModifiedBy", "Editor"),
        ("cp:revision", "3"),
        ("dc:language", "en-GB"),
        ("dcterms:created", "2020-01-02T03:04:05Z"),
        ("dcterms:modified", "2021-06-07T08:09:10Z"),
        ("cp:lastPrinted", "2019-12-31T23:59:58Z"));

    /// <summary>An application-level part that populates every published Word statistic.</summary>
    public static string FullApp() => App(
        ("Template", "Normal.dotm"),
        ("Pages", "5"),
        ("Words", "1234"),
        ("Characters", "6789"),
        ("Application", "Microsoft Office Word"),
        ("Company", "SkyCD Ltd"),
        ("Manager", "M. Manager"),
        ("AppVersion", "16.0000"));

    /// <summary>An application-level part for a presentation, carrying the slide count instead of page counts.</summary>
    public static string PresentationApp(int slides) => App(
        ("Application", "Microsoft Office PowerPoint"),
        ("Slides", slides.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("Words", "200"),
        ("Paragraphs", "30"));

    public static byte[] Truncate(byte[] data, int bytes) => data.AsSpan(0, data.Length - bytes).ToArray();

    /// <summary>Points the first directory sector outside the file, so the compound chain cannot be walked.</summary>
    public static byte[] DamageDirectory(byte[] data)
    {
        data[48] = 0xFF;
        data[49] = 0xFF;
        data[50] = 0xFF;
        data[51] = 0xFF;
        return data;
    }

    // ---- OLE2 compound documents ----

    /// <summary>A legacy Word document identified by its <c>WordDocument</c> stream.</summary>
    public static byte[] LegacyWord(params SummaryProperty[] summary) =>
        Compound(
            new CompoundStream("WordDocument", Marker(16)),
            new CompoundStream(SummaryStream, PropertySet(summary)),
            new CompoundStream(DocumentSummaryStream, PropertySet(FullDocumentSummary())));

    public static byte[] LegacyWordWithDocumentSummary(SummaryProperty[] summary, SummaryProperty[] documentSummary) =>
        Compound(
            new CompoundStream("WordDocument", Marker(16)),
            new CompoundStream(SummaryStream, PropertySet(summary)),
            new CompoundStream(DocumentSummaryStream, PropertySet(documentSummary)));

    /// <summary>A legacy Excel workbook identified by its <c>Workbook</c> stream.</summary>
    public static byte[] LegacyWorkbook() =>
        Compound(new CompoundStream("Workbook", Marker(16)));

    /// <summary>A legacy Excel workbook identified by the older <c>Book</c> stream.</summary>
    public static byte[] LegacyBook() =>
        Compound(new CompoundStream("Book", Marker(16)));

    /// <summary>A legacy PowerPoint presentation.</summary>
    public static byte[] LegacyPresentation() =>
        Compound(new CompoundStream("PowerPoint Document", Marker(16)));

    /// <summary>An OLE container with no Office document stream at all.</summary>
    public static byte[] CompoundWithoutOfficeStream() =>
        Compound(new CompoundStream("Other", Marker(16)));

    /// <summary>The wrapper Office 2007 and later put around an encrypted OOXML package.</summary>
    public static byte[] EncryptedPackage() =>
        Compound(
            new CompoundStream(EncryptionInfoStream, Marker(32)),
            new CompoundStream(EncryptedPackageStream, Marker(64)));

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
        FileTime(11, LastPrinted),
        FileTime(12, Created),
        FileTime(13, Modified),
        Int4(14, 5),
        Int4(15, 1234),
        Int4(16, 6789),
        Text(18, "Microsoft Office Word")
    ];

    public static SummaryProperty[] FullDocumentSummary() =>
    [
        Codepage(),
        Text(2, "Reports"),
        Text(14, "M. Manager"),
        Text(15, "SkyCD Ltd"),
        Text(27, "Final"),
        Text(28, "en-GB")
    ];

    public static SummaryProperty Codepage(int codePage = 1252) =>
        new(1, 0x0002, [(byte)codePage, (byte)(codePage >> 8), 0, 0]);

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

    private static byte[] Marker(int length) => new byte[length];

    private static void Add(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Writes a compound file with one FAT sector, one directory sector and both regular and mini streams.
    /// The builder supports at most four directory entries (root plus three streams).
    /// </summary>
    private static byte[] Compound(params CompoundStream[] streams)
    {
        if (streams.Length > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(streams),
                "The fixture keeps every directory entry in one sector.");
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
