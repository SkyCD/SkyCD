using System;
using System.IO;
using System.Threading;
using SkyCD.Plugin.AbeMeda.Native.Exceptions;
using SkyCD.Plugin.AbeMeda.Native.Models;

namespace SkyCD.Plugin.AbeMeda.Native.Parsing;

/// <summary>
/// Reads and validates the fixed header. Validation order is signature, version
/// policy, sentinel, layout constants, then all section/entry bounds with
/// checked arithmetic.
/// </summary>
public static class AbeMedaNativeHeaderReader
{
    public static AbeMedaNativeHeader Read(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanRead || !source.CanSeek)
        {
            throw new AbeMedaNativeUnsupportedSchemaException(
                "the source stream must be readable and seekable.");
        }

        var length = source.Length;
        if (length < AbeMedaNativeLayout.MinimumHeaderBytes)
        {
            throw new AbeMedaNativeCorruptCatalogException(
                $"the fixed header needs at least {AbeMedaNativeLayout.MinimumHeaderBytes} bytes but the file has {length}.");
        }

        var reader = new AbeMedaNativeBinaryReader(source, length);
        var signature = reader.ReadBytes(0, AbeMedaNativeLayout.SignatureLength);
        if (!signature.AsSpan().SequenceEqual("DWDRDNBA"u8))
        {
            throw new AbeMedaNativeUnsupportedSchemaException(
                $"file signature is {FormatSignature(signature)} instead of '{AbeMedaNativeLayout.SignatureText}'.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var header = new AbeMedaNativeHeader(
            Version: reader.ReadUInt32(AbeMedaNativeLayout.VersionOffset),
            CompatibilityVersion: reader.ReadUInt32(AbeMedaNativeLayout.CompatibilityVersionOffset),
            Integrity: reader.ReadUInt32(AbeMedaNativeLayout.IntegrityOffset),
            Unknown2C: reader.ReadUInt32(AbeMedaNativeLayout.Unknown2COffset),
            Unknown30: reader.ReadUInt32(AbeMedaNativeLayout.Unknown30Offset),
            Unknown34: reader.ReadUInt32(AbeMedaNativeLayout.Unknown34Offset),
            Unknown38: reader.ReadUInt32(AbeMedaNativeLayout.Unknown38Offset),
            HeaderSize: reader.ReadUInt32(AbeMedaNativeLayout.HeaderSizeOffset),
            EntryCount: reader.ReadUInt32(AbeMedaNativeLayout.EntryCountOffset),
            EntryStride: reader.ReadUInt32(AbeMedaNativeLayout.EntryStrideOffset),
            SectionOffset: reader.ReadUInt32(AbeMedaNativeLayout.SectionOffsetOffset),
            SectionLength: reader.ReadUInt32(AbeMedaNativeLayout.SectionLengthOffset),
            BlockSize: reader.ReadUInt32(AbeMedaNativeLayout.BlockSizeOffset));

        Validate(header, reader, length);
        return header;
    }

    private static void Validate(AbeMedaNativeHeader header, AbeMedaNativeBinaryReader reader, long length)
    {
        if (header.Version != AbeMedaNativeLayout.SupportedVersion ||
            header.CompatibilityVersion != AbeMedaNativeLayout.SupportedVersion)
        {
            throw new AbeMedaNativeUnsupportedVersionException(header.Version, header.CompatibilityVersion);
        }

        var sentinel = reader.ReadUInt32(AbeMedaNativeLayout.SentinelOffset);
        if (sentinel != AbeMedaNativeLayout.SentinelValue)
        {
            throw new AbeMedaNativeCorruptCatalogException(
                $"header sentinel is 0x{sentinel:X8}; expected 0x{AbeMedaNativeLayout.SentinelValue:X8}.");
        }

        if (header.HeaderSize != AbeMedaNativeLayout.SupportedHeaderSize)
        {
            throw new AbeMedaNativeUnsupportedLayoutException(
                $"header size is {header.HeaderSize}; expected {AbeMedaNativeLayout.SupportedHeaderSize}");
        }

        if (header.EntryStride != AbeMedaNativeLayout.SupportedEntryStride)
        {
            throw new AbeMedaNativeUnsupportedLayoutException(
                $"entry stride is {header.EntryStride}; expected {AbeMedaNativeLayout.SupportedEntryStride}");
        }

        if (header.BlockSize == 0 || header.BlockSize > AbeMedaNativeLayout.MaxBlockSize)
        {
            throw new AbeMedaNativeCorruptCatalogException(
                $"block size {header.BlockSize} is outside the supported range.");
        }

        if (header.EntryCount > AbeMedaNativeLayout.MaxEntries)
        {
            throw new AbeMedaNativeCorruptCatalogException(
                $"the header declares {header.EntryCount} entries; the limit is {AbeMedaNativeLayout.MaxEntries}.");
        }

        if (header.SectionOffset < header.HeaderSize)
        {
            throw new AbeMedaNativeCorruptCatalogException(
                $"section offset 0x{header.SectionOffset:X} precedes the header end 0x{header.HeaderSize:X}.");
        }

        var sectionEnd = (ulong)header.SectionOffset + header.SectionLength;
        if (sectionEnd > (ulong)length)
        {
            throw new AbeMedaNativeCorruptCatalogException(
                $"section [0x{header.SectionOffset:X}, 0x{sectionEnd:X}) exceeds the file length {length}.");
        }

        var entryTableEnd = (ulong)header.HeaderSize + (ulong)header.EntryCount * header.EntryStride;
        if (entryTableEnd > header.SectionOffset)
        {
            throw new AbeMedaNativeCorruptCatalogException(
                $"the declared entry table ends at 0x{entryTableEnd:X}, past the section start 0x{header.SectionOffset:X}.");
        }
    }

    private static string FormatSignature(ReadOnlySpan<byte> signature)
    {
        var text = new System.Text.StringBuilder(signature.Length);
        foreach (var value in signature)
        {
            text.Append(value is >= 0x20 and <= 0x7E ? (char)value : '.');
        }

        return $"'{text}' (0x{Convert.ToHexString(signature)})";
    }
}
