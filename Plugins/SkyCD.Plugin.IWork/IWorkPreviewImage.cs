using System;

namespace SkyCD.Plugin.IWork;

/// <summary>
/// Reads the pixel dimensions from the header of a PNG or JPEG preview without decoding the image.
/// </summary>
internal static class IWorkPreviewImage
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static (long Width, long Height)? TryReadDimensions(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return ReadPng(data);
        }

        return ReadJpeg(data);
    }

    private static (long Width, long Height)? ReadPng(ReadOnlySpan<byte> data)
    {
        var width = ReadBigEndianUInt32(data.Slice(16, 4));
        var height = ReadBigEndianUInt32(data.Slice(20, 4));
        return width > 0 && height > 0 ? (width, height) : null;
    }

    private static (long Width, long Height)? ReadJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return null;
        }

        var offset = 2;
        while (offset + 3 < data.Length)
        {
            while (offset < data.Length && data[offset] != 0xFF)
            {
                offset++;
            }

            while (offset < data.Length && data[offset] == 0xFF)
            {
                offset++;
            }

            if (offset >= data.Length)
            {
                return null;
            }

            var marker = data[offset++];
            if (marker is 0x01 or (>= 0xD0 and <= 0xD9))
            {
                continue;
            }

            if (offset + 1 >= data.Length)
            {
                return null;
            }

            var segmentLength = (data[offset] << 8) | data[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > data.Length)
            {
                return null;
            }

            if (IsStartOfFrame(marker) && segmentLength >= 7)
            {
                var height = (data[offset + 3] << 8) | data[offset + 4];
                var width = (data[offset + 5] << 8) | data[offset + 6];
                return width > 0 && height > 0 ? (width, height) : null;
            }

            offset += segmentLength;
        }

        return null;
    }

    private static bool IsStartOfFrame(byte marker) =>
        marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE
            or 0xCF;

    private static long ReadBigEndianUInt32(ReadOnlySpan<byte> data) =>
        ((long)data[0] << 24) | ((long)data[1] << 16) | ((long)data[2] << 8) | data[3];
}
