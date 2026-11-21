using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Executable.Parsing;

namespace SkyCD.Plugin.Executable.Tests;

/// <summary>
/// Builds synthetic portable executables in memory. The bytes are assembled field by field so every test
/// states the exact header, section and resource layout it depends on, and no binary fixture is checked in.
/// </summary>
/// <remarks>
/// Layout produced by <see cref="Build"/>: DOS stub of <see cref="DosStubSize"/> bytes, the PE header, one
/// section table entry, then a single <c>.rsrc</c> section whose virtual address equals its file offset.
/// The resource section starts with a packed three-level tree — type, name, language — where every directory
/// entry carries its offset in the high bit of its target, and data entries point at the resource payloads
/// that follow the tree.
/// </remarks>
internal static class ExecutableFixtures
{
    public const int DosStubSize = 0x40;
    public const uint SectionRva = 0x1000;
    public const ushort MachineX64 = 0x8664;
    public const ushort MachineX86 = 0x014C;

    public const int ImageTypeCursor = 1;
    public const int ImageTypeIcon = 3;
    public const int ImageTypeIconGroup = 14;
    public const int ImageTypeVersion = 16;

    /// <summary>One icon image as a group declares it; <paramref name="Data"/> is the raw resource payload.</summary>
    public readonly record struct IconEntry(byte Width, byte Height, ushort BitCount, byte[] Data)
    {
        public static IconEntry Dib(int width, int height, byte[] data) =>
            new(Checked(width), Checked(height), 32, data);

        public static IconEntry Png(int width, int height, byte[] data) =>
            new(Checked(width), Checked(height), 32, data);

        /// <summary>Widths and heights above 255 are written as zero, the icon format's encoding of 256.</summary>
        private static byte Checked(int size) =>
            size switch
            {
                >= 0 and <= 255 => (byte)size,
                256 => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(size))
            };
    }

    /// <summary>A named resource of the given type and numeric name.</summary>
    public sealed record Resource(int Type, int Name, byte[] Data)
    {
        /// <summary>The payloads a group declares, in declared order; empty for ordinary resource types.</summary>
        public IReadOnlyList<IconEntry> Images { get; init; } = [];
    }

    public static readonly Resource NoResources = new(ImageTypeIcon, 1, []);

    /// <summary>A complete executable with no resources at all.</summary>
    public static byte[] Build(uint resourceRva = 0, int resourceSize = 0, ushort machine = MachineX64,
        bool pe32Plus = true, bool executable = true, bool dotNet = false)
    {
        var resources = resourceRva == 0 ? Array.Empty<byte>() : new byte[Math.Max(resourceSize, 512)];
        return BuildCore(resources, resourceRva, machine, pe32Plus, executable, dotNet);
    }

    /// <summary>An executable whose single icon group references the given images.</summary>
    public static byte[] WithIcons(params IconEntry[] entries) =>
        BuildGroups(Group(1, entries));

    /// <summary>
    /// An executable with the given resources and arbitrarily named icon groups. A group is paired with the
    /// icon the index-th image of the group declares, so callers state only the group and its images.
    /// </summary>
    public static byte[] BuildGroups(params Resource[] groups)
    {
        var resources = new List<(int Type, int Name, byte[] Data)>();
        foreach (var group in groups)
        {
            resources.Add((group.Type, group.Name, group.Data));
            var index = 0;
            foreach (var image in group.Images)
            {
                resources.Add((ImageTypeIcon, ++index, image.Data));
            }
        }

        return Build(resources);
    }

    /// <summary>An executable with exactly the given resources, no implicit icon pairing.</summary>
    public static byte[] BuildResources(params Resource[] resources) =>
        Build(resources.Select(resource => (resource.Type, resource.Name, resource.Data)).ToList());

    /// <summary>An icon group node with its declared image payloads.</summary>
    public static Resource Group(int name, params IconEntry[] images) =>
        new(ImageTypeIconGroup, name, GroupDirectory(images)) { Images = images };

    public static byte[] WithVersion(string fileVersion, string productName, string companyName) =>
        BuildResources(
            new Resource(ImageTypeVersion, 1,
                VersionInfo(fileVersion, productName, companyName)),
            new Resource(ImageTypeIconGroup, 1, GroupDirectory(
                [IconEntry.Dib(16, 16, Pixel32Dib(16, 16, 0x102030, 2))])),
            new Resource(ImageTypeIcon, 1, Pixel32Dib(16, 16, 0x102030, 2)));

    /// <summary>
    /// Builds a full image from a flat resource list. The tree is laid out as four contiguous regions — root,
    /// type directories, name directories, language leaves — followed by the data entries and the payloads, so
    /// every tag is an offset relative to the section that the host resolves with plain arithmetic.
    /// </summary>
    public static byte[] Build(IReadOnlyList<(int Type, int Name, byte[] Data)> resources)
    {
        if (resources.Count == 0)
        {
            return BuildCore([], 0, MachineX64, pe32Plus: true, executable: true, dotNet: false);
        }

        var types = new SortedDictionary<int, SortedDictionary<int, byte[]>>();
        foreach (var (type, name, data) in resources)
        {
            if (!types.TryGetValue(type, out var byName))
            {
                types[type] = byName = new SortedDictionary<int, byte[]>();
            }

            byName[name] = data;
        }

        var count = resources.Count;
        var rootBase = 0;
        var typeBase = rootBase + 16 + (8 * types.Count);
        var nameBase = typeBase + (types.Count * (16 + 8));
        var leafBase = nameBase + (count * (16 + 8));
        var dataEntriesBase = leafBase + (count * (16 + 8));
        var payloadBase = Align16(dataEntriesBase + (count * 16));

        var section = new MemoryStream();
        section.SetLength(payloadBase);
        section.Position = rootBase;
        WriteDirectoryHeader(section, rootBase, 0, types.Count);

        var typeIndex = 0;
        var nameIndex = 0;
        var payloadLength = 0;
        foreach (var (type, byName) in types)
        {
            var nameDirectoryBase = nameBase + (nameIndex * (16 + 8));
            WriteTag(section, rootBase + 16 + (typeIndex * 8), type,
                nameDirectoryBase | unchecked((int)0x80000000));
            WriteDirectoryHeader(section, nameDirectoryBase, 0, byName.Count);

            var nameCursor = nameIndex;
            foreach (var (name, data) in byName)
            {
                var languageBase = leafBase + (nameCursor * (16 + 8));
                WriteTag(section, nameDirectoryBase + 16 + ((nameCursor - nameIndex) * 8), name,
                    languageBase | unchecked((int)0x80000000));
                WriteDirectoryHeader(section, languageBase, 0, 1);
                WriteTag(section, languageBase + 16, 0, dataEntriesBase + (nameCursor * 16));

                section.Position = dataEntriesBase + (nameCursor * 16);
                var directory = new byte[16];
                BinaryPrimitives.WriteUInt32LittleEndian(directory, SectionRva + (uint)payloadBase + (uint)payloadLength);
                BinaryPrimitives.WriteUInt32LittleEndian(directory.AsSpan(4), (uint)data.Length);
                section.Write(directory);

                nameCursor++;
                payloadLength = Align16(payloadLength + data.Length);
            }

            nameIndex += byName.Count;
            typeIndex++;
        }

        // Payloads are padded so that each one starts on a 16-byte boundary, as the resource format expects.
        section.Position = payloadBase;
        var written = 0;
        foreach (var byName in types.Values)
        {
            foreach (var data in byName.Values)
            {
                section.Write(data);
                written += data.Length;
                var padding = Align16(written) - written;
                section.Write(new byte[padding]);
                written += padding;
            }
        }

        var bytes = section.ToArray();
        return BuildCore(bytes, SectionRva, MachineX64, pe32Plus: true, executable: true, dotNet: false);
    }

    private static void WriteTag(Stream stream, int offset, int id, int target)
    {
        stream.Position = offset;
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, id);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], target);
        stream.Write(buffer);
    }

    private static void WriteDirectoryHeader(Stream stream, int offset, int namedEntries, int idEntries)
    {
        stream.Position = offset;
        Span<byte> buffer = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, 0);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[12..], (ushort)namedEntries);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[14..], (ushort)idEntries);
        stream.Write(buffer);
    }

    private static int Align16(int value) => (value + 15) & ~15;

    /// <summary>Wraps a resource section into a complete PE image.</summary>
    private static byte[] BuildCore(byte[] resourceSection, uint resourceRva, ushort machine, bool pe32Plus,
        bool executable, bool dotNet)
    {
        var optionalSize = pe32Plus ? 240 : 224;
        var headerSize = DosStubSize + 24 + optionalSize + 40;
        var rawSection = Align16(resourceSection.Length);
        if (rawSection > 0)
        {
            headerSize = Align16(headerSize);
        }

        var image = new byte[headerSize + rawSection];
        var span = image.AsSpan();

        // DOS header: the signature and the PE header offset are the only fields a loader needs.
        "MZ"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], DosStubSize);

        // PE signature and COFF header.
        "PE\0\0"u8.CopyTo(span[DosStubSize..]);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(DosStubSize + 4)..], machine);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(DosStubSize + 6)..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(DosStubSize + 20)..], (ushort)optionalSize);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(DosStubSize + 22)..], executable ? (ushort)0x0002 : (ushort)0x2000);

        // Optional header: magic, the executable characteristic bit and the data directories we use.
        var optional = DosStubSize + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(span[optional..], pe32Plus ? (ushort)0x20B : (ushort)0x10B);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(optional + 70)..], 0x0002);
        var directoryCountOffset = optional + (pe32Plus ? 0x6C : 0x5C);
        BinaryPrimitives.WriteUInt32LittleEndian(span[directoryCountOffset..], 16);
        var directories = optional + (pe32Plus ? 0x70 : 0x60);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(directories + (2 * 8))..], resourceRva);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(directories + (2 * 8) + 4)..], (uint)resourceSection.Length);
        if (dotNet)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[(directories + (14 * 8))..], 0x2000);
            BinaryPrimitives.WriteUInt32LittleEndian(span[(directories + (14 * 8) + 4)..], 0x48);
        }

        // One section: virtual address equals its file offset so data entries can use section-relative RVA math.
        var sectionTable = optional + optionalSize;
        ".rsrc\0\0\0"u8.CopyTo(span[sectionTable..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(sectionTable + 8)..], (uint)rawSection);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(sectionTable + 12)..], SectionRva);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(sectionTable + 16)..], (uint)resourceSection.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[(sectionTable + 20)..], (uint)rawSection);
        BinaryPrimitives.WriteInt32LittleEndian(span[(sectionTable + 36)..], headerSize);

        resourceSection.CopyTo(span[headerSize..]);
        return image;
    }

    /// <summary>An <c>GRPICONDIR</c>: reserved, type, count, then one 14-byte entry per image.</summary>
    public static byte[] GroupDirectory(IReadOnlyList<IconEntry> entries)
    {
        var buffer = new byte[6 + (entries.Count * 14)];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            var (width, height, bitCount, data) = entries[index];
            var entry = buffer.AsSpan(6 + (index * 14));
            entry[0] = width;
            entry[1] = height;
            entry[2] = 0;
            entry[3] = 0;
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], bitCount);
            BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)data.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[12..], (ushort)(index + 1));
        }

        return buffer;
    }

    /// <summary>Icon resources for the given entries, named from <paramref name="firstName"/> upward.</summary>
    public static Resource[] Pack(IReadOnlyList<IconEntry> entries, int firstName)
    {
        var resources = new Resource[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            resources[index] = new Resource(ImageTypeIcon, firstName + index, entries[index].Data);
        }

        return resources;
    }

    /// <summary>A 32-bit BGRA icon bitmap in a <c>BITMAPINFOHEADER</c>, rows bottom-up then a logical AND mask.</summary>
    public static byte[] Pixel32Dib(int width, int height, int rgba, int cornerWidth)
    {
        var red = (byte)(rgba >> 16);
        var green = (byte)(rgba >> 8);
        var blue = (byte)rgba;
        var stride = width * 4;
        var maskStride = ((width + 31) / 32) * 4;
        var data = new byte[40 + (stride * height) + (maskStride * height)];
        BinaryPrimitives.WriteInt32LittleEndian(data, 40);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), height * 2);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), (uint)(stride * height));
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = 40 + ((y * stride) + (x * 4));
                var transparent = x < cornerWidth && y >= height - cornerWidth;
                data[offset] = blue;
                data[offset + 1] = green;
                data[offset + 2] = red;
                data[offset + 3] = (byte)(transparent ? 0 : 255);
            }
        }

        // The AND mask is bottom-up as well, so its first rows are the rendered image's bottom rows; the
        // transparent corner is drawn in the top-left, which lands in its last rows.
        for (var y = 0; y < cornerWidth; y++)
        {
            var row = height - cornerWidth + y;
            for (var x = 0; x < cornerWidth; x++)
            {
                data[40 + (stride * height) + (row * maskStride) + (x / 8)] |= (byte)(1 << (7 - (x % 8)));
            }
        }

        return data;
    }

    /// <summary>An icon image stored as a PNG, reusing the plugin's own encoder so no fixture binary is needed.</summary>
    public static byte[] PngImage(int width, int height, uint rgba, int borderWidth = 0)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var border = x < borderWidth || y < borderWidth;
                var value = border ? rgba | 0x000000FFu : rgba;
                var offset = ((y * width) + x) * 4;
                pixels[offset] = (byte)(value >> 24);
                pixels[offset + 1] = (byte)(value >> 16);
                pixels[offset + 2] = (byte)(value >> 8);
                pixels[offset + 3] = (byte)value;
            }
        }

        return PngDecoder.Encode(width, height, pixels);
    }

    /// <summary>A <c>VS_VERSION_INFO</c> resource with a fixed block and one string table.</summary>
    public static byte[] VersionInfo(string fileVersion, string productName, string companyName)
    {
        var strings = new List<(string Key, string Value)>
        {
            ("CompanyName", companyName),
            ("FileDescription", "Executable under test"),
            ("FileVersion", fileVersion),
            ("InternalName", "skycd-fixture"),
            ("LegalCopyright", "Copyright (c) SkyCD"),
            ("OriginalFilename", "skycd-fixture.exe"),
            ("ProductName", productName),
            ("ProductVersion", fileVersion)
        };

        var stringTable = new MemoryStream();
        foreach (var (key, value) in strings)
        {
            AppendStringNode(stringTable, key, value);
        }

        var stringTableNode = Node("040904B0", [], valueLengthInWords: false, stringTable.ToArray());
        var stringFileInfo = Node("StringFileInfo", [], valueLengthInWords: false, stringTableNode);
        var translation = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(translation, 0x0409);
        BinaryPrimitives.WriteUInt16LittleEndian(translation.AsSpan(2), 0x04B0);
        var varFileInfo = Node("VarFileInfo", [],
            valueLengthInWords: false, Node("Translation", translation, valueLengthInWords: true));

        var fixedInfo = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(fixedInfo, 0xFEEF04BD);
        BinaryPrimitives.WriteUInt32LittleEndian(fixedInfo.AsSpan(4), 0x00010000);
        WriteVersion(fixedInfo.AsSpan(8), fileVersion);
        WriteVersion(fixedInfo.AsSpan(16), fileVersion);

        // The fixed block is the value of VS_VERSION_INFO itself, declared in bytes; every string node below it
        // declares its value in UTF-16 words, as the resource format specifies.
        return Node("VS_VERSION_INFO", fixedInfo, valueLengthInWords: false, stringFileInfo, varFileInfo);
    }

    private static void WriteVersion(Span<byte> target, string version)
    {
        var parts = version.Split('.');

        // Each version integer keeps its major word high and its minor word low, so the dotted parts are packed
        // two per 32-bit value rather than written one after another.
        for (var index = 0; index < 2; index++)
        {
            var high = Part(parts, index * 2);
            var low = Part(parts, (index * 2) + 1);
            BinaryPrimitives.WriteUInt32LittleEndian(target[(index * 4)..], ((uint)high << 16) | low);
        }
    }

    private static ushort Part(string[] parts, int index) =>
        index < parts.Length && ushort.TryParse(parts[index], out var parsed) ? parsed : (ushort)0;

    private static byte[] Utf16(string text) => Encoding.Unicode.GetBytes(text);

    private static byte[] Node(string key, byte[] value, bool valueLengthInWords, params byte[][] children)
    {
        var stream = new MemoryStream();
        var declared = valueLengthInWords ? value.Length / 2 : value.Length;
        AppendNodeHeader(stream, key, declared, children.Sum(child => child.Length));
        stream.Write(value);
        Pad(stream);
        foreach (var child in children)
        {
            stream.Write(child);
        }

        var node = stream.ToArray();

        // The declared length covers the whole node including children, so it is patched once they are known.
        BinaryPrimitives.WriteUInt16LittleEndian(node, (ushort)node.Length);
        return node;
    }

    private static void AppendStringNode(Stream stream, string key, string value)
    {
        var start = stream.Position;
        var text = Utf16(value + "\0");
        AppendNodeHeader(stream, key, text.Length / 2, childrenLength: 0);
        stream.Write(text);
        Pad(stream);
        var end = stream.Position;
        stream.Position = start;
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)(end - start));
        stream.Write(length);
        stream.Position = end;
    }

    private static void AppendNodeHeader(Stream stream, string key, int valueLength, int childrenLength)
    {
        Span<byte> header = stackalloc byte[6];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)(6 + ((key.Length + 1) * 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], (ushort)valueLength);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], 1);
        stream.Write(header);
        stream.Write(Utf16(key + "\0"));
        while (stream.Position % 4 != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static void Pad(Stream stream)
    {
        while (stream.Position % 4 != 0)
        {
            stream.WriteByte(0);
        }
    }
}
