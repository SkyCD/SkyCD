using System;
using System.IO;

namespace SkyCD.Plugin.Adc;

/// <summary>
/// Content-aware recognition for Advanced Disk Catalog databases.
/// </summary>
/// <remarks>
/// Recognition combines the file extension with the documented header: the 32-bit
/// length <c>11</c>, the ASCII signature <c>ADC.Catalog</c>, major version <c>1</c>,
/// plausible record/offset fields, and an explicit rejection of Windows Authenticode
/// security catalogs, which also use the <c>.cat</c> extension.
/// </remarks>
public static class AdcDetector
{
    private static readonly string[] SupportedExtensions = [".cat", ".adc"];

    public static bool HasSupportedExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return true;
        }

        var extension = Path.GetExtension(fileName.Trim());
        foreach (var supported in SupportedExtensions)
        {
            if (string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Recognizes a DER-encoded PKCS#7 SignedData catalog, the shape of Windows security
    /// catalogs. Long-form ASN.1 SEQUENCE length is required to avoid claiming arbitrary data.
    /// </summary>
    public static bool IsAuthenticodeCatalog(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data[0] != 0x30)
        {
            return false;
        }

        return (data[1] & 0x80) != 0;
    }

    public static bool HasCatalogHeader(Stream stream)
    {
        var header = new byte[AdcFormat.HeaderSize];
        if (ReadFull(stream, header) < header.Length)
        {
            return false;
        }

        if (!HasCatalogHeader(header))
        {
            return false;
        }

        if (stream.CanSeek)
        {
            var length = (ulong)stream.Length;
            if (ReadUInt64(header, AdcFormat.BodyOffsetOffset) > length ||
                ReadUInt64(header, AdcFormat.CommentTableOffsetOffset) > length)
            {
                return false;
            }
        }

        return true;
    }

    public static bool HasCatalogHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < AdcFormat.HeaderSize || IsAuthenticodeCatalog(header))
        {
            return false;
        }

        if (ReadUInt32(header, 0) != AdcFormat.SignatureLength)
        {
            return false;
        }

        for (var i = 0; i < AdcFormat.Signature.Length; i++)
        {
            if (header[AdcFormat.SignatureOffset + i] != AdcFormat.Signature[i])
            {
                return false;
            }
        }

        if (ReadUInt16(header, AdcFormat.VersionMajorOffset) != AdcFormat.SupportedVersionMajor)
        {
            return false;
        }

        if (ReadUInt32(header, AdcFormat.RecordCountOffset) > AdcFormat.MaxRecordCount)
        {
            return false;
        }

        var bodyOffset = ReadUInt64(header, AdcFormat.BodyOffsetOffset);
        var commentTableOffset = ReadUInt64(header, AdcFormat.CommentTableOffsetOffset);
        return bodyOffset >= AdcFormat.HeaderSize && commentTableOffset >= bodyOffset;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static ulong ReadUInt64(ReadOnlySpan<byte> data, int offset) =>
        ReadUInt32(data, offset) | ((ulong)ReadUInt32(data, offset + 4) << 32);

    private static int ReadFull(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
