using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.SDK.Capabilities.Media;
using static SkyCD.Plugin.Metadata.Image.Parsing.ContainerReader;
using static MetadataExtractor.Formats.Exif.ExifDirectoryBase;

namespace SkyCD.Plugin.Metadata.Image.Parsing.Raw;

internal static class RawImageReader
{
    public static async Task<ContainerValues?> ReadMetadata(Input input)
    {
        var data = await input.Prefix(HeaderBytes).ConfigureAwait(false);
        var directories = ReadDirectories(data);
        var ifd = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        if (ifd is null && exif is null && !HasRawHeader(data))
        {
            return null;
        }

        var values = new ContainerValues(input.Extension);
        var width = GetInteger(exif, TagExifImageWidth) ?? GetInteger(ifd, TagImageWidth);
        var height = GetInteger(exif, TagExifImageHeight) ?? GetInteger(ifd, TagImageHeight);
        values.Size(width ?? 0, height ?? 0);
        var orientation = GetOrientation(ifd);
        values.Integer("Orientation", orientation);
        values.Integer("RotationDegrees", ImageDecoder.RotationDegrees((SKEncodedOrigin)orientation));
        values.Text("CameraMake", ifd?.GetString(TagMake));
        values.Text("CameraModel", ifd?.GetString(TagModel));
        values.Text("LensModel", exif?.GetString(TagLensModel));
        values.Integer("Iso", GetInteger(exif, TagIsoEquivalent));
        values.Text("Aperture", exif?.GetDescription(TagFNumber));
        values.Text("ExposureTime", exif?.GetDescription(TagExposureTime));
        values.Integer("FocalLength", GetInteger(exif, TagFocalLength));
        values.Text("Software", ifd?.GetString(TagSoftware));
        values.AddEmbedded(EmbeddedMetadataReader.Read(data));
        return values;
    }

    public static async Task<MediaPreviewArtifact?> ReadPreview(Input input)
    {
        var data = await input.Prefix(ScanBytes).ConfigureAwait(false);
        var header = data[..Math.Min(data.Length, HeaderBytes)];
        var ifd = ReadDirectories(header).OfType<ExifIfd0Directory>().FirstOrDefault();
        if (ifd is null && !HasRawHeader(header))
        {
            return null;
        }

        var orientation = GetOrientation(ifd);
        var embedded = EmbeddedPreviewReader.Jpeg(data, input.Token, orientation);
        return embedded ?? NativeImageReader.RawThumbnail(
            await input.Whole(DecodeBytes).ConfigureAwait(false), input, orientation);
    }

    private static bool HasRawHeader(ReadOnlySpan<byte> data) =>
        data.StartsWith("II\u002a\0"u8) || data.StartsWith("MM\0\u002a"u8) ||
        data.StartsWith("FUJIFILMCCD-RAW "u8) || data.StartsWith("IIRO"u8) ||
        data.StartsWith("IIRS"u8) || data.StartsWith("IIU\0"u8) ||
        (data.Length >= 14 && data.Slice(6, 8).SequenceEqual("HEAPCCDR"u8));

    private static int? GetInteger(ExifDirectoryBase? directory, int tag) =>
        directory is not null && directory.TryGetInt32(tag, out var value) ? value : null;

    private static int GetOrientation(ExifIfd0Directory? directory) =>
        GetInteger(directory, TagOrientation) is >= 1 and <= 8 and var orientation ? orientation : 1;

    private static System.Collections.Generic.IReadOnlyList<MetadataExtractor.Directory> ReadDirectories(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            return ImageMetadataReader.ReadMetadata(stream);
        }
        catch (Exception exception) when (exception is ImageProcessingException or IOException or ArgumentException)
        {
            return [];
        }
    }
}
