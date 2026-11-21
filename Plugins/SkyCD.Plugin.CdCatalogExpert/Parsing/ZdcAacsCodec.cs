using System;
using System.IO;
using System.IO.Compression;
using SkyCD.Plugin.CdCatalogExpert.Exceptions;
using SkyCD.Plugin.CdCatalogExpert.Models;

namespace SkyCD.Plugin.CdCatalogExpert.Parsing;

/// <summary>
/// AACS member-payload wrapper used by ZDC for XML and JPEG member bytes.
/// </summary>
/// <remarks>
/// Layout of the wrapped form: <c>AACS</c> magic, profile value, zero, one,
/// <c>0x40000000</c>, compression field (<c>5</c> for ZDC), zero padding through
/// offset <c>0x7F</c>, compressed byte count, uncompressed byte count, CRC-16
/// of the uncompressed bytes, total wrapper size (<c>0x90 + compressed</c>),
/// then the RFC 1950 zlib stream. No process execution or native dependency is
/// used; compression goes through the BCL <see cref="ZLibStream"/>.
/// </remarks>
public static class ZdcAacsCodec
{
    /// <summary>
    /// Wraps raw payload bytes in a deterministic AACS envelope.
    /// </summary>
    public static byte[] Wrap(byte[] payload, uint profile)
    {
        ArgumentNullException.ThrowIfNull(payload);

        byte[] compressed;
        using (var output = new MemoryStream())
        {
            using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(payload, 0, payload.Length);
            }

            compressed = output.ToArray();
        }

        var wrapper = new byte[ZdcFormat.AacsHeaderSize + compressed.Length];
        ZdcFormat.AacsMagic.CopyTo(wrapper, 0);
        WriteUInt32(wrapper, 0x04, profile);
        WriteUInt32(wrapper, 0x08, 0);
        WriteUInt32(wrapper, 0x0C, ZdcFormat.AacsRequiredOne);
        WriteUInt32(wrapper, 0x10, ZdcFormat.AacsRequiredMagic2);
        WriteUInt32(wrapper, 0x14, ZdcFormat.AacsCompressionField);
        WriteUInt32(wrapper, 0x80, (uint)compressed.Length);
        WriteUInt32(wrapper, 0x84, (uint)payload.Length);
        WriteUInt32(wrapper, 0x88, ZdcChecksums.Crc16(payload, 0, payload.Length));
        WriteUInt32(wrapper, 0x8C, (uint)wrapper.Length);
        compressed.CopyTo(wrapper, ZdcFormat.AacsHeaderSize);
        return wrapper;
    }

    /// <summary>
    /// Validates an AACS envelope trimmed to the member's stored length and
    /// returns the decompressed payload.
    /// </summary>
    public static byte[] Unwrap(byte[] stored, string memberName)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(memberName);

        Ensure(stored.Length >= ZdcFormat.AacsHeaderSize, memberName, "wrapped payload is shorter than the AACS header.");
        for (var i = 0; i < ZdcFormat.AacsMagic.Length; i++)
        {
            if (stored[i] != ZdcFormat.AacsMagic[i])
            {
                throw new ZdcCorruptException($"member '{memberName}' is missing the AACS signature.");
            }
        }

        Ensure(ReadUInt32(stored, 0x08) == 0, memberName, "AACS reserved field is not zero.");
        Ensure(ReadUInt32(stored, 0x0C) == ZdcFormat.AacsRequiredOne, memberName, "AACS constant field is not one.");
        Ensure(ReadUInt32(stored, 0x10) == ZdcFormat.AacsRequiredMagic2, memberName, "AACS constant field is not 0x40000000.");
        Ensure(ReadUInt32(stored, 0x14) == ZdcFormat.AacsCompressionField, memberName, "unsupported AACS compression field; only ZDC field 5 is supported.");

        for (var i = 0x18; i < 0x80; i++)
        {
            if (stored[i] != 0)
            {
                throw new ZdcCorruptException($"member '{memberName}' has non-zero AACS padding.");
            }
        }

        var compressedCount = ReadUInt32(stored, 0x80);
        var uncompressedCount = ReadUInt32(stored, 0x84);
        var expectedCrc = (ushort)ReadUInt32(stored, 0x88);
        var wrapperSize = ReadUInt32(stored, 0x8C);

        if (uncompressedCount > ZdcFormat.MaxInflatedBytes)
        {
            throw new ZdcCorruptException($"member '{memberName}' declares {uncompressedCount} inflated bytes, exceeding the safety limit.");
        }

        Ensure(wrapperSize == (uint)stored.Length, memberName, "AACS wrapper size disagrees with the stored member length.");
        Ensure(compressedCount == (uint)stored.Length - ZdcFormat.AacsHeaderSize, memberName, "AACS compressed length disagrees with the stored member length.");

        var payload = new byte[uncompressedCount];
        try
        {
            using (var input = new MemoryStream(stored, ZdcFormat.AacsHeaderSize, (int)compressedCount, writable: false))
            using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
            {
                var total = 0;
                while (total < payload.Length)
                {
                    var read = zlib.Read(payload, total, payload.Length - total);
                    if (read == 0)
                    {
                        throw new ZdcCorruptException($"member '{memberName}' zlib stream ends before the declared inflated length.");
                    }

                    total += read;
                }

                if (zlib.ReadByte() != -1)
                {
                    throw new ZdcCorruptException($"member '{memberName}' zlib stream holds data past the declared inflated length.");
                }
            }
        }
        catch (InvalidDataException exception)
        {
            throw new ZdcCorruptException($"member '{memberName}' zlib stream is invalid: {exception.Message}");
        }

        var actualCrc = ZdcChecksums.Crc16(payload, 0, payload.Length);
        if (actualCrc != expectedCrc)
        {
            throw new ZdcCorruptException($"member '{memberName}' fails CRC validation.");
        }

        return payload;
    }

    private static void Ensure(bool condition, string memberName, string detail)
    {
        if (!condition)
        {
            throw new ZdcCorruptException($"member '{memberName}' {detail}");
        }
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
