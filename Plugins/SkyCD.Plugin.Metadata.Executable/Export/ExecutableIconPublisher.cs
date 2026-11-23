using System;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Executable.Exceptions;
using SkyCD.Plugin.Metadata.Executable.Models;

namespace SkyCD.Plugin.Metadata.Executable.Export;

/// <summary>
/// Turns a decoded platform icon into the single bounded PNG artifact the catalog stores.
/// </summary>
/// <remarks>
/// Every platform source ends up as the same RGBA image, so the scale-down and encode path — and the byte cap
/// it enforces — is shared instead of repeated per source. No image and an icon that cannot be encoded within
/// the bounds both produce the explicit empty artifact list, which keeps the item's type icon.
/// </remarks>
internal static class ExecutableIconPublisher
{
    public static MediaPreviewResult Publish(ExecutableIconImage? image, ExecutablePluginOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (image is null)
        {
            return new MediaPreviewResult([]);
        }

        try
        {
            var png = ExecutableIconEncoder.TryEncode(image, options.IconSize, options.MaxPngBytes);
            return png is null
                ? new MediaPreviewResult([])
                : new MediaPreviewResult(
                    [new MediaPreviewArtifact(ExecutableIconPlugin.PngContentType, new ReadOnlyMemory<byte>(png))]);
        }
        catch (ExecutableIconException)
        {
            return new MediaPreviewResult([]);
        }
    }
}
