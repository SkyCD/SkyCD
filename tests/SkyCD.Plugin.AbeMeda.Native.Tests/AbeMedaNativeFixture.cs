using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.AbeMeda.Native.Tests;

public sealed record SyntheticEntry(
    ulong Ordinal,
    ulong BlobValue,
    uint Flags,
    string Handler,
    uint RangeA,
    uint RangeB,
    byte[]? RawHandler = null);

/// <summary>
/// Builds a synthetic classic container using only the offsets documented for
/// the 7.8.7 demo catalog. No vendor bytes are stored or redistributed.
/// </summary>
public sealed class SyntheticCatalog
{
    public string Magic { get; set; } = AbeMedaNativeLayout.SignatureText;

    public uint Version { get; set; } = AbeMedaNativeLayout.SupportedVersion;

    public uint CompatibilityVersion { get; set; } = AbeMedaNativeLayout.SupportedVersion;

    public uint Integrity { get; set; } = 0xC4E69DE6;

    public uint Unknown2C { get; set; } = 0x01E72E65;

    public uint Unknown30 { get; set; } = 0x0210E067;

    public uint Unknown34 { get; set; } = 6;

    public uint Unknown38 { get; set; } = 10;

    public uint? SentinelOverride { get; set; }

    public uint HeaderSize { get; set; } = AbeMedaNativeLayout.SupportedHeaderSize;

    public uint? DeclaredEntryCount { get; set; }

    public uint EntryStride { get; set; } = AbeMedaNativeLayout.SupportedEntryStride;

    public uint? SectionOffset { get; set; }

    public uint SectionLength { get; set; } = 4096;

    public uint? HeaderSectionOffsetOverride { get; set; }

    public uint? HeaderSectionLengthOverride { get; set; }

    public uint BlockSize { get; set; } = 4096;

    public int? TruncateTo { get; set; }

    public List<SyntheticEntry> Entries { get; } = [];

    public byte[] Build()
    {
        var stride = checked((int)EntryStride);
        if (stride < 36)
        {
            throw new InvalidOperationException("The synthetic stride must fit the documented 36-byte record.");
        }

        var entryBytes = (long)Entries.Count * stride;
        var sectionOffset = SectionOffset ?? checked((uint)(HeaderSize + entryBytes + 32));
        var fileLength = checked((long)sectionOffset + SectionLength);
        var bytes = new byte[fileLength];

        WriteAscii(bytes, 0, Magic);
        WriteUInt32(bytes, AbeMedaNativeLayout.VersionOffset, Version);
        WriteUInt32(bytes, AbeMedaNativeLayout.IntegrityOffset, Integrity);
        WriteUInt32(bytes, AbeMedaNativeLayout.CompatibilityVersionOffset, CompatibilityVersion);
        WriteUInt32(bytes, AbeMedaNativeLayout.Unknown2COffset, Unknown2C);
        WriteUInt32(bytes, AbeMedaNativeLayout.Unknown30Offset, Unknown30);
        WriteUInt32(bytes, AbeMedaNativeLayout.Unknown34Offset, Unknown34);
        WriteUInt32(bytes, AbeMedaNativeLayout.Unknown38Offset, Unknown38);
        WriteUInt32(bytes, AbeMedaNativeLayout.SentinelOffset,
            SentinelOverride ?? AbeMedaNativeLayout.SentinelValue);
        WriteUInt32(bytes, AbeMedaNativeLayout.HeaderSizeOffset, HeaderSize);
        WriteUInt32(bytes, AbeMedaNativeLayout.EntryCountOffset,
            DeclaredEntryCount ?? checked((uint)Entries.Count));
        WriteUInt32(bytes, AbeMedaNativeLayout.EntryStrideOffset, EntryStride);
        WriteUInt32(bytes, AbeMedaNativeLayout.SectionOffsetOffset,
            HeaderSectionOffsetOverride ?? sectionOffset);
        WriteUInt32(bytes, AbeMedaNativeLayout.SectionLengthOffset,
            HeaderSectionLengthOverride ?? SectionLength);
        WriteUInt32(bytes, AbeMedaNativeLayout.BlockSizeOffset, BlockSize);

        for (var index = 0; index < Entries.Count; index++)
        {
            var entry = Entries[index];
            var offset = checked((int)(HeaderSize + (long)index * stride));
            WriteUInt64(bytes, offset + AbeMedaNativeLayout.EntryOrdinalOffset, entry.Ordinal);
            WriteUInt64(bytes, offset + AbeMedaNativeLayout.EntryBlobValueOffset, entry.BlobValue);
            WriteUInt32(bytes, offset + AbeMedaNativeLayout.EntryFlagsOffset, entry.Flags);
            WriteHandler(bytes, offset + AbeMedaNativeLayout.EntryHandlerOffset, entry);
            WriteUInt32(bytes, offset + AbeMedaNativeLayout.EntryRangeAOffset, entry.RangeA);
            WriteUInt32(bytes, offset + AbeMedaNativeLayout.EntryRangeBOffset, entry.RangeB);
        }

        for (var index = (int)sectionOffset; index < bytes.Length; index++)
        {
            bytes[index] = 0x5A;
        }

        if (TruncateTo is { } truncated && truncated < bytes.Length)
        {
            Array.Resize(ref bytes, truncated);
        }

        return bytes;
    }

    private static void WriteHandler(byte[] bytes, int offset, SyntheticEntry entry)
    {
        var handler = entry.RawHandler;
        if (handler is null)
        {
            var text = Encoding.ASCII.GetBytes(entry.Handler);
            handler = new byte[AbeMedaNativeLayout.HandlerLength];
            Array.Copy(text, handler, Math.Min(text.Length, handler.Length));
        }

        Array.Copy(handler, 0, bytes, offset, Math.Min(handler.Length, AbeMedaNativeLayout.HandlerLength));
    }

    private static void WriteAscii(byte[] bytes, int offset, string value)
    {
        var text = Encoding.ASCII.GetBytes(value);
        Array.Copy(text, 0, bytes, offset, Math.Min(text.Length, bytes.Length - offset));
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    }

    private static void WriteUInt64(byte[] bytes, int offset, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset, 8), value);
    }
}

public static class AbeMedaNativeFixture
{
    public const string DefaultFileName = "demo.cdw";

    public static MemoryStream CreateCatalog(Action<SyntheticCatalog>? configure = null)
    {
        var catalog = new SyntheticCatalog();
        catalog.Entries.Add(new SyntheticEntry(1, 1024, 0, "JPEGGKON", 0x00000100, 0x00000200));
        catalog.Entries.Add(new SyntheticEntry(2, 2048, 1, "PNGfGKON", 0x00000300, 0x00000400));
        catalog.Entries.Add(new SyntheticEntry(3, 4096, 2, "MPG3hook", 0x00000500, 0x00000600));

        configure?.Invoke(catalog);
        return new MemoryStream(catalog.Build(), writable: false);
    }

    public static MemoryStream CreateEmptyCatalog()
    {
        return CreateCatalog(catalog => catalog.Entries.Clear());
    }

    public static FileFormatReadRequest ReadRequest(Stream stream, string fileName = DefaultFileName)
    {
        return new FileFormatReadRequest
        {
            FormatId = "abemeda-native",
            Source = stream,
            FileName = fileName
        };
    }
}
