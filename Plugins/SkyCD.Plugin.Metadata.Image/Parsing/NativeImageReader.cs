using System;
using System.Collections.Generic;
using System.Linq;
using ImageMagick;
using ImageMagick.Formats;
using SkyCD.SDK.Capabilities.Media;
using static SkyCD.Plugin.Metadata.Image.Parsing.ContainerReader;

namespace SkyCD.Plugin.Metadata.Image.Parsing;

internal static class NativeImageReader
{
    private static readonly Lazy<HashSet<MagickFormat>> Formats = new(Initialize);

    public static void EnsureInitialized() => _ = Formats.Value;

    public static bool CanRead(MagickFormat format) => Formats.Value.Contains(format);

    private static HashSet<MagickFormat> Initialize()
    {
        try
        {
            MagickNET.SetEnvironmentVariable("MAGICK_MAP_LIMIT", "512MiB");
            ResourceLimits.Memory = 256 * MiB;
            ResourceLimits.Disk = 0;
            ResourceLimits.Width = 8192;
            ResourceLimits.Height = 8192;
            ResourceLimits.Time = 30;
            return MagickNET.SupportedFormats.Where(format => format.SupportsReading)
                .Select(format => format.Format).ToHashSet();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
            or TypeInitializationException or BadImageFormatException)
        {
            return [];
        }
    }

    public static MediaPreviewArtifact? Composite(byte[]? data, MagickFormat format, Input input)
    {
        if (data is null || format is not (MagickFormat.Psd or MagickFormat.Psb or MagickFormat.Xcf
            or MagickFormat.Wmf or MagickFormat.Emf) || !CanRead(format))
        {
            return null;
        }

        input.Token.ThrowIfCancellationRequested();
        var settings = new MagickReadSettings { Format = format, FrameIndex = 0, FrameCount = 1 };
        using var image = new MagickImage(data, settings);
        image.AutoOrient();
        var (width, height) = ImageDecoder.ScaleTo((int)image.Width, (int)image.Height, ThumbnailEdge);
        if (width != image.Width || height != image.Height)
        {
            image.Resize((uint)width, (uint)height);
        }

        return Thumbnail(image.ToByteArray(MagickFormat.Png), input.Token);
    }

    public static MediaPreviewArtifact? RawThumbnail(byte[]? data, Input input, int orientation)
    {
        if (data is null || !CanRead(MagickFormat.Dng))
        {
            return null;
        }

        input.Token.ThrowIfCancellationRequested();
        var settings = new MagickReadSettings { Format = MagickFormat.Dng };
        settings.SetDefines(new DngReadDefines { ReadThumbnail = true, MaxRawMemory = 256 });
        using var image = new MagickImage();
        image.Ping(data, settings);
        return Thumbnail(image.GetProfile("dng:thumbnail")?.ToByteArray(), input.Token, orientation);
    }
}
