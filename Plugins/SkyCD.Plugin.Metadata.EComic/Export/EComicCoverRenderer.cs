using System;
using System.Threading;
using SkyCD.Plugin.Metadata.EComic.Exceptions;
using SkyCD.Plugin.Metadata.EComic.Models;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.EComic.Export;

/// <summary>
/// Turns an encoded comic page into an opaque JPEG cover whose longest edge is at most the configured size.
/// </summary>
/// <remarks>
/// Validation and messages stay this plugin's own; the bounded decode, scale and encode pipeline is shared
/// with the other cover-producing plugins through <see cref="CoverImageRenderer"/>.
/// </remarks>
internal static class EComicCoverRenderer
{
    public static byte[] Render(byte[] page, int coverSize, EComicPluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(coverSize);
        ArgumentNullException.ThrowIfNull(options);

        return CoverImageRenderer.Render(
            page,
            coverSize,
            options.MaxPagePixels,
            options.JpegQuality,
            "the page",
            message => new EComicCoverException(message),
            cancellationToken);
    }

    /// <summary>Uniform downscale that never upscales and never drops an edge to zero pixels.</summary>
    internal static (int Width, int Height) ScaleTo(int width, int height, int maxDimension) =>
        CoverImageRenderer.ScaleTo(width, height, maxDimension);
}
