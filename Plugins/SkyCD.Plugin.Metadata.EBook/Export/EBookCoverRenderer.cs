using System;
using System.Threading;
using SkyCD.Plugin.Metadata.EBook.Exceptions;
using SkyCD.Plugin.Metadata.EBook.Models;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.EBook.Export;

/// <summary>
/// Turns an embedded cover image into an opaque JPEG whose longest edge is at most the configured size.
/// </summary>
/// <remarks>
/// Validation and messages stay this plugin's own; the bounded decode, scale and encode pipeline is shared
/// with the other cover-producing plugins through <see cref="CoverImageRenderer"/>.
/// </remarks>
internal static class EBookCoverRenderer
{
    public static byte[] Render(byte[] cover, int coverSize, EBookPluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cover);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(coverSize);
        ArgumentNullException.ThrowIfNull(options);

        return CoverImageRenderer.Render(
            cover,
            coverSize,
            options.MaxCoverPixels,
            options.JpegQuality,
            "the cover",
            message => new EBookCoverException(message),
            cancellationToken);
    }

    /// <summary>Uniform downscale that never upscales and never drops an edge to zero pixels.</summary>
    internal static (int Width, int Height) ScaleTo(int width, int height, int maxDimension) =>
        CoverImageRenderer.ScaleTo(width, height, maxDimension);
}
