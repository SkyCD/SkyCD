using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using SkyCD.Plugin.Metadata.Office.Exceptions;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads the thumbnail a document embeds for browsing and Quick Look.
/// </summary>
/// <remarks>
/// An OOXML package publishes its preview as <c>docProps/thumbnail.jpeg</c>, so those bytes are copied after
/// their JPEG structure and pixel bounds are checked. A legacy compound document keeps the same picture in the
/// <c>SummaryInformation</c> property set as clipboard data; only the device-independent bitmap form is
/// published, wrapped in a bitmap container, while metafile variants stay unrendered because no rasterizer is
/// added. Nothing is decrypted: an encrypted package, a missing or damaged part and an unsupported variant all
/// yield <see langword="null"/>, which the host records as an explicit "no preview". The containers that hold no
/// thumbnail at all, such as a cabinet-packed notebook or a Jet database, end the same way rather than as an
/// error, because the absence of a picture is the answer. A source that holds no Office container of any kind is
/// still a decode failure, the same verdict the metadata capability reaches for it.
/// </remarks>
internal static class OfficeThumbnailReader
{
    private const string PackageThumbnailPart = "docProps/thumbnail.jpeg";

    private const uint ThumbnailPropertyId = 17;
    private const uint VtCf = 0x0047;
    private const uint ClipFormatDib = 8;
    private const uint ClipFormatDibV5 = 17;

    private const int MaxProperties = 1024;
    private const int BitmapFileHeaderBytes = 14;
    private const int BitmapInfoHeaderBytes = 40;

    public static OfficeThumbnail? Read(
        byte[] data,
        string? extension,
        OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);

        if (OfficeContainers.IsPackage(data))
        {
            return ReadPackageThumbnail(data, options, cancellationToken);
        }

        if (CompoundFile.HasSignature(data))
        {
            return ReadCompoundThumbnail(data, options, cancellationToken);
        }

        // A notebook package and a database are Office containers that hold no picture at all.
        if (OfficeContainers.IsCabinet(data) || OfficeContainers.IsJet(data))
        {
            return null;
        }

        // The interchange plan is plain text, which is the one Office source with no container to inspect.
        if (OfficeFamilyResolver.FromExtension(extension) is OfficeFamily.Plan)
        {
            return null;
        }

        throw new OfficeDecodeException(
            "the source is neither an OOXML package, an Office compound document nor a OneNote, Access or Project file");
    }

    private static OfficeThumbnail? ReadPackageThumbnail(byte[] data, OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var entry = FindEntry(archive, PackageThumbnailPart);
            if (entry is null || entry.Length <= 0 || entry.Length > options.MaxPreviewBytes)
            {
                return null;
            }

            var bytes = ReadBounded(entry, options.MaxPreviewBytes, cancellationToken);
            return bytes is null ? null : ReadJpeg(bytes, options.MaxThumbnailDimension);
        }
        catch (InvalidDataException exception)
        {
            throw new OfficeDecodeException("the package structure could not be read", exception);
        }
    }

    /// <summary>Part names inside a package differ in case between producers, so the lookup ignores case.</summary>
    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string name)
    {
        foreach (var entry in archive.Entries)
        {
            if (string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>Reads a member, refusing one whose decompressed size exceeds the bound.</summary>
    private static byte[]? ReadBounded(ZipArchiveEntry entry, long maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var source = entry.Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            long total = 0;
            int read;
            while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                total += read;
                if (total > maxBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static OfficeThumbnail? ReadCompoundThumbnail(byte[] data, OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // An encrypted OOXML package wraps the real document: nothing in it may be read without a credential.
        var compound = CompoundFile.Open(data);
        if (OfficeSourceReader.IsEncryptedWrapper(compound))
        {
            return null;
        }

        var limit = Math.Max(options.MaxMetadataPartBytes, options.MaxPreviewBytes);
        try
        {
            return compound.TryReadStream(OfficeSourceReader.SummaryStream, limit, out var summary)
                ? ReadSummaryThumbnail(summary, options)
                : null;
        }
        catch (OfficeDecodeException)
        {
            // The thumbnail is optional: a damaged summary chain costs the preview, not the extraction.
            return null;
        }
    }

    /// <summary>Publishes a JPEG only when its SOF header is readable and both edges are inside the bound.</summary>
    private static OfficeThumbnail? ReadJpeg(byte[] bytes, int maxDimension)
    {
        if (!TryReadJpegSize(bytes, out var width, out var height)
            || width <= 0 || height <= 0 || width > maxDimension || height > maxDimension)
        {
            return null;
        }

        return new OfficeThumbnail("image/jpeg", bytes);
    }

    private static bool TryReadJpegSize(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return false;
        }

        var offset = 2;
        while (offset + 3 < data.Length)
        {
            if (data[offset] != 0xFF)
            {
                return false;
            }

            var marker = data[offset + 1];
            offset += 2;

            if (marker == 0xFF)
            {
                // A fill byte belongs to the same marker; only one byte was consumed.
                offset--;
                continue;
            }

            if (marker == 0x00)
            {
                return false;
            }

            // Standalone markers carry no length: TEM, RST0-7, SOI and EOI.
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
            {
                continue;
            }

            var length = (data[offset] << 8) | data[offset + 1];
            if (length < 2 || offset + length > data.Length)
            {
                return false;
            }

            // Any start-of-frame marker except DHT, JPG and DAC carries the picture size.
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (length < 7)
                {
                    return false;
                }

                height = (data[offset + 3] << 8) | data[offset + 4];
                width = (data[offset + 5] << 8) | data[offset + 6];
                return true;
            }

            offset += length;
        }

        return false;
    }

    private static OfficeThumbnail? ReadSummaryThumbnail(byte[] stream, OfficePluginOptions options)
    {
        if (stream.Length < 48 || ReadU16(stream, 0) != 0xFFFE)
        {
            return null;
        }

        var setCount = ReadU32(stream, 24);
        if (setCount is 0 or > 2)
        {
            return null;
        }

        var headerLength = 28 + (20 * (int)setCount);
        if (stream.Length < headerLength)
        {
            return null;
        }

        var sectionOffset = ReadU32(stream, 44);
        if (sectionOffset < headerLength || sectionOffset > int.MaxValue)
        {
            return null;
        }

        var section = (int)sectionOffset;
        if (section + 8 > stream.Length)
        {
            return null;
        }

        var sectionSize = ReadU32(stream, section);
        var propertyCount = ReadU32(stream, section + 4);
        if (sectionSize < 8 || sectionSize > (uint)(stream.Length - section) || propertyCount > MaxProperties
            || 8 + ((long)propertyCount * 8) > sectionSize)
        {
            return null;
        }

        var sectionEnd = section + (int)sectionSize;
        var table = section + 8;
        for (var index = 0; index < propertyCount; index++)
        {
            var entry = table + (index * 8);
            if (ReadU32(stream, entry) != ThumbnailPropertyId)
            {
                continue;
            }

            var valueOffset = ReadU32(stream, entry + 4);
            if (valueOffset < 8 || valueOffset > int.MaxValue)
            {
                return null;
            }

            var value = (long)section + valueOffset;
            if (value + 8 > sectionEnd || ReadU32(stream, (int)value) != VtCf)
            {
                return null;
            }

            return ReadClipboardThumbnail(stream, (int)value + 4, sectionEnd, options);
        }

        return null;
    }

    /// <summary>Reads the clipboard payload of a <c>VT_CF</c> property and decodes it when it is a bitmap.</summary>
    private static OfficeThumbnail? ReadClipboardThumbnail(byte[] stream, int payload, int sectionEnd,
        OfficePluginOptions options)
    {
        if (payload + 8 > sectionEnd)
        {
            return null;
        }

        // The clipboard blob is a size, a format tag and the format data; the size covers the tag as well.
        var size = ReadU32(stream, payload);
        var format = ReadU32(stream, payload + 4);
        if (format is not (ClipFormatDib or ClipFormatDibV5) || size < 4)
        {
            return null;
        }

        var dataStart = payload + 8;
        var length = (long)size - 4;
        if (length <= 0 || length > options.MaxPreviewBytes || length > sectionEnd - dataStart)
        {
            return null;
        }

        return WrapDibAsBitmap(stream.AsSpan(dataStart, (int)length), options);
    }

    /// <summary>Adds the bitmap file header a packed DIB lacks, so the host can decode it as <c>image/bmp</c>.</summary>
    private static OfficeThumbnail? WrapDibAsBitmap(ReadOnlySpan<byte> dib, OfficePluginOptions options)
    {
        if (dib.Length < BitmapInfoHeaderBytes)
        {
            return null;
        }

        var headerSize = (int)ReadU32(dib, 0);
        if (headerSize < BitmapInfoHeaderBytes || headerSize > dib.Length)
        {
            return null;
        }

        var width = ReadI32(dib, 4);
        var height = ReadI32(dib, 8);
        if (width <= 0 || height is 0 or int.MinValue)
        {
            return null;
        }

        var pixelHeight = Math.Abs(height);
        var bitCount = ReadU16(dib, 14);
        var compression = ReadU32(dib, 16);
        if (compression > 3 || width > options.MaxThumbnailDimension
            || pixelHeight > options.MaxThumbnailDimension)
        {
            return null;
        }

        // A palette entry is four bytes in this header; a header without explicit masks carries three masks.
        var paletteBytes = bitCount is 1 or 4 or 8 ? (1L << bitCount) * 4 : 0;
        if (bitCount <= 8 && paletteBytes == 0)
        {
            return null;
        }

        if (compression == 3 && headerSize == BitmapInfoHeaderBytes)
        {
            paletteBytes += 12;
        }

        var bitsOffset = BitmapFileHeaderBytes + headerSize + paletteBytes;
        var fileSize = (long)BitmapFileHeaderBytes + dib.Length;
        if (bitsOffset > fileSize || fileSize > options.MaxPreviewBytes)
        {
            return null;
        }

        var bitmap = new byte[(int)fileSize];
        bitmap[0] = (byte)'B';
        bitmap[1] = (byte)'M';
        WriteU32(bitmap, 2, (uint)fileSize);
        WriteU32(bitmap, 10, (uint)bitsOffset);
        dib.CopyTo(bitmap.AsSpan(BitmapFileHeaderBytes));
        return new OfficeThumbnail("image/bmp", bitmap);
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static int ReadI32(ReadOnlySpan<byte> data, int offset) => (int)ReadU32(data, offset);

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }
}
