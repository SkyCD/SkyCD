using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Image.Exceptions;

namespace SkyCD.Plugin.Image;

/// <summary>
/// Rejects sources the plugin should not attempt to read, before any byte is charged against the host
/// budget. The host's own read budget remains authoritative for anything this guard lets through.
/// </summary>
internal static class ImageSourceGuard
{
    public static void Validate(MediaItem item, IMediaSource source, long maxSourceBytes)
    {
        var length = source.Length > 0 ? source.Length : item.Length;
        if (length <= 0)
        {
            throw new ImageDecodeException("the source is empty");
        }

        if (length > maxSourceBytes)
        {
            throw new ImageSourceTooLargeException(length, maxSourceBytes);
        }
    }
}
