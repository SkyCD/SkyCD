using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ImageMagick;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.SDK.Capabilities.Media;
using static SkyCD.Plugin.Metadata.Image.Parsing.ContainerReader;

namespace SkyCD.Plugin.Metadata.Image.Parsing.Photoshop;

internal static class PhotoshopDocumentReader
{
    private static readonly string?[] ColorModes =
        ["Bitmap", "Grayscale", "Indexed", "RGB", "CMYK", null, null, "Multichannel", "Duotone", "Lab"];

    public static async Task<ContainerValues?> ReadMetadata(Input input)
    {
        var data = await input.Prefix(HeaderBytes).ConfigureAwait(false);
        if (!ValidHeader(data, input.Extension))
        {
            return null;
        }

        var values = new ContainerValues(input.Extension);
        values.Size(U32(data.AsSpan(18)), U32(data.AsSpan(14)));
        values.Integer("BitDepth", U16(data.AsSpan(22)));
        var mode = U16(data.AsSpan(24));
        values.Text("ColorMode", mode < ColorModes.Length ? ColorModes[mode] : null);
        values.Integer("LayerCount", ReadLayerCount(data, input.Extension == "psb"));
        ReadDocumentProperties(data, values);
        values.AddEmbedded(EmbeddedMetadataReader.Read(data));
        return values;
    }

    public static async Task<MediaPreviewArtifact?> ReadPreview(Input input)
    {
        var data = await input.Prefix(ScanBytes).ConfigureAwait(false);
        if (!ValidHeader(data, input.Extension))
        {
            return null;
        }

        var embedded = Thumbnail(FindThumbnail(data, 1036), input.Token)
            ?? Thumbnail(FindThumbnail(data, 1033), input.Token);
        return embedded ?? NativeImageReader.Composite(await input.Whole(DecodeBytes).ConfigureAwait(false),
            input.Extension == "psb" ? MagickFormat.Psb : MagickFormat.Psd, input);
    }

    private static bool ValidHeader(ReadOnlySpan<byte> data, string extension) =>
        data.Length >= 26 && data.StartsWith("8BPS"u8) && U16(data[4..]) == (extension == "psb" ? 2 : 1);

    private static byte[]? FindThumbnail(ReadOnlySpan<byte> data, int resourceId)
    {
        if (data.Length < 34 || U32(data[26..]) > data.Length - 34)
        {
            return null;
        }

        var resources = data[(30 + (int)U32(data[26..]))..];
        resources = resources.Slice(4, (int)Math.Min(U32(resources), resources.Length - 4L));
        while (resources.Length >= 12 && resources.StartsWith("8BIM"u8))
        {
            var nameLength = (resources[6] + 2) & ~1;
            if (resources.Length < 10 + nameLength)
            {
                break;
            }

            var length = U32(resources[(6 + nameLength)..]);
            var payload = resources[(10 + nameLength)..];
            if (length > payload.Length)
            {
                break;
            }

            if (U16(resources[4..]) == resourceId && length is >= 28 and <= CandidateBytes)
            {
                var preview = payload[..(int)length];
                var width = U32(preview[4..]);
                var height = U32(preview[8..]);
                var stride = U32(preview[12..]);
                if (U32(preview) == 0 && resourceId == 1033 && width is > 0 and <= 8192 &&
                    height is > 0 and <= 8192 && stride >= width * 3 && (long)stride * height <= preview.Length - 28)
                {
                    using var bitmap = new SKBitmap((int)width, (int)height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    var pixels = bitmap.GetPixelSpan();
                    for (var y = 0; y < (int)height; y++)
                    {
                        var row = preview.Slice(28 + y * (int)stride, (int)width * 3);
                        for (var x = 0; x < (int)width; x++)
                        {
                            var bgr = row.Slice(x * 3, 3);
                            var index = y * bitmap.RowBytes + x * 4;
                            bgr.CopyTo(pixels[index..]);
                            pixels[index + 3] = 255;
                        }
                    }
                    using var image = SKImage.FromBitmap(bitmap);
                    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
                    return png?.ToArray();
                }
                if (U32(preview) == 1 && preview[28..].StartsWith(new byte[] { 255, 216, 255 }))
                {
                    if (resourceId == 1036)
                    {
                        return preview[28..].ToArray();
                    }

                    // Photoshop 4 stores this JPEG with RGB and BGR interchanged.
                    using var bytes = SKData.CreateCopy(preview[28..].ToArray());
                    using var codec = SKCodec.Create(bytes);
                    if (codec is null || codec.Info.Width > 8192 || codec.Info.Height > 8192)
                    {
                        return null;
                    }
                    using var bitmap = SKBitmap.Decode(codec);
                    if (bitmap is null)
                    {
                        return null;
                    }
                    using var canvas = new SKCanvas(bitmap);
                    using var image = SKImage.FromBitmap(bitmap);
                    using var filter = SKColorFilter.CreateColorMatrix(
                        [0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0]);
                    using var paint = new SKPaint { ColorFilter = filter, BlendMode = SKBlendMode.Src };
                    canvas.DrawImage(image, 0, 0, paint);
                    using var corrected = SKImage.FromBitmap(bitmap);
                    using var png = corrected.Encode(SKEncodedImageFormat.Png, 100);
                    return png?.ToArray();
                }
            }

            if (length + (length & 1) > payload.Length)
            {
                break;
            }
            resources = payload[(int)(length + (length & 1))..];
        }

        return null;
    }

    private static int? ReadLayerCount(ReadOnlySpan<byte> data, bool largeDocument)
    {
        if (data.Length < 34 || U32(data[26..]) > data.Length - 34)
        {
            return null;
        }
        var resources = data[(30 + (int)U32(data[26..]))..];
        if (U32(resources) > resources.Length - 4)
        {
            return null;
        }
        var layers = resources[(4 + (int)U32(resources))..];
        var lengthSize = largeDocument ? 8 : 4;
        if (layers.Length < lengthSize)
        {
            return null;
        }
        ulong ReadLength(ReadOnlySpan<byte> bytes) => largeDocument
            ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : U32(bytes);
        var sectionLength = ReadLength(layers);
        if (sectionLength == 0)
        {
            return 0;
        }
        if (sectionLength < (ulong)lengthSize || layers.Length < 2 * lengthSize)
        {
            return null;
        }
        var infoLength = ReadLength(layers[lengthSize..]);
        return infoLength == 0 ? 0 : infoLength >= 2 && layers.Length >= 2 * lengthSize + 2
            ? Math.Abs((int)BinaryPrimitives.ReadInt16BigEndian(layers[(2 * lengthSize)..])) : null;
    }

    private static void ReadDocumentProperties(byte[] bytes, ContainerValues values)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var directories = ImageMetadataReader.ReadMetadata(stream);
            var iptc = directories.OfType<IptcDirectory>().FirstOrDefault();
            var exif = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            values.Text("Title", iptc?.GetString(IptcDirectory.TagObjectName) ?? exif?.GetString(ExifDirectoryBase.TagWinTitle));
            values.Text("Author", iptc?.GetString(IptcDirectory.TagByLine) ?? exif?.GetString(ExifDirectoryBase.TagArtist));
            values.Text("Software", exif?.GetString(ExifDirectoryBase.TagSoftware));
        }
        catch (Exception exception) when (exception is ImageProcessingException or IOException or ArgumentException)
        {
            // The PSD header still supplies geometry when the resource section is incomplete.
        }
    }

    private static ushort U16(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt16BigEndian(data);
    private static uint U32(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt32BigEndian(data);
}
