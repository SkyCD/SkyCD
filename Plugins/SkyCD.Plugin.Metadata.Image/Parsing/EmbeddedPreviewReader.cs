using System;
using System.Buffers.Binary;
using System.Threading;
using SkiaSharp;
using SkyCD.SDK.Capabilities.Media;
using static SkyCD.Plugin.Metadata.Image.Parsing.ContainerReader;

namespace SkyCD.Plugin.Metadata.Image.Parsing;

internal static class EmbeddedPreviewReader
{
    private static ReadOnlySpan<byte> JpegStart => [0xff, 0xd8, 0xff];
    private static ReadOnlySpan<byte> PngStart => [137, 80, 78, 71, 13, 10, 26, 10];

    public static MediaPreviewArtifact? Jpeg(byte[] data, CancellationToken token, int orientation) =>
        Thumbnail(Scan(data, JpegStart, SKEncodedImageFormat.Jpeg, token), token, orientation);

    public static MediaPreviewArtifact? Png(byte[] data, CancellationToken token) =>
        Thumbnail(FindPng(data, token), token);

    internal static byte[]? FindPng(byte[] data, CancellationToken token) => Scan(data, PngStart, SKEncodedImageFormat.Png, token);

    private static byte[]? Scan(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature,
        SKEncodedImageFormat format, CancellationToken token)
    {
        ReadOnlySpan<byte> best = default;
        long bestArea = 0;
        var candidateCount = 0;
        while (!data.IsEmpty && candidateCount < 256)
        {
            token.ThrowIfCancellationRequested();
            var offset = data.IndexOf(signature);
            if (offset < 0)
            {
                break;
            }

            candidateCount++;
            data = data[offset..];
            var candidate = data[..Math.Min(data.Length, CandidateBytes)];
            var length = format == SKEncodedImageFormat.Png ? PngLength(candidate) : JpegLength(candidate);
            if (length > 0)
            {
                using var bytes = SKData.CreateCopy(candidate[..length].ToArray());
                using var codec = SKCodec.Create(bytes);
                if (codec is not null && codec.EncodedFormat == format &&
                    codec.Info.Width is > 0 and <= 8192 && codec.Info.Height is > 0 and <= 8192)
                {
                    var area = (long)codec.Info.Width * codec.Info.Height;
                    if (area > bestArea)
                    {
                        best = candidate[..length];
                        bestArea = area;
                    }
                }
            }
            data = data[signature.Length..];
        }

        return best.IsEmpty ? null : best.ToArray();
    }

    private static int JpegLength(ReadOnlySpan<byte> data)
    {
        // Skip length-delimited sections so an EXIF thumbnail's EOI does not truncate the outer JPEG.
        var offset = 2;
        var inScan = false;
        while (offset < data.Length)
        {
            if (inScan)
            {
                var markerOffset = data[offset..].IndexOf((byte)0xff);
                if (markerOffset < 0)
                {
                    break;
                }
                offset += markerOffset;
            }
            else if (data[offset] != 0xff)
            {
                break;
            }

            do
            {
                offset++;
            } while (offset < data.Length && data[offset] == 0xff);
            if (offset == data.Length)
            {
                break;
            }
            var marker = data[offset++];
            if (marker == 0xd9)
            {
                return inScan ? offset : 0;
            }
            if (inScan && (marker == 0 || marker is >= 0xd0 and <= 0xd7))
            {
                continue;
            }
            if (marker is 0 or 0xd8 || data.Length - offset < 2)
            {
                break;
            }
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
            if (length < 2 || length > data.Length - offset)
            {
                break;
            }
            offset += length;
            inScan = marker == 0xda;
        }
        return 0;
    }

    private static int PngLength(ReadOnlySpan<byte> data)
    {
        var offset = PngStart.Length;
        while (data.Length - offset >= 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > data.Length - offset - 12)
            {
                break;
            }

            if (length == 0 && data.Slice(offset + 4, 4).SequenceEqual("IEND"u8))
            {
                return offset + 12;
            }
            offset += (int)length + 12;
        }

        return 0;
    }
}
