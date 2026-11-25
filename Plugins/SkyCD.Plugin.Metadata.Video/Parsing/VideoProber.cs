using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Video.Exceptions;
using SkyCD.Plugin.Metadata.Video.Models;

namespace SkyCD.Plugin.Metadata.Video.Parsing;

/// <summary>
/// Identifies the container from its bytes and dispatches to the matching reader.
/// </summary>
/// <remarks>
/// Container recognition is deliberately separate from codec availability: a container this plugin can read
/// is read whatever codec sits inside it, and an unrecognized codec is published as its raw identifier rather
/// than treated as a failure. Whether a poster frame can be produced is a further, independent question,
/// answered by <see cref="IVideoFrameExtractor"/>.
/// </remarks>
internal static class VideoProber
{
    public static async Task<VideoProbe> ProbeAsync(
        MediaItem item,
        IMediaSource source,
        VideoPluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var reader = new VideoSourceReader(source, item);
        var header = await reader.ReadAsync(0, 64, cancellationToken).ConfigureAwait(false);
        var container = DetectContainer(header.Span, item.Extension);

        return container switch
        {
            "mp4" or "mov" => await Mp4VideoReader.ReadAsync(reader, options, cancellationToken).ConfigureAwait(false),
            "matroska" => await MatroskaReader.ReadAsync(reader, options, cancellationToken).ConfigureAwait(false),
            "avi" => await AviReader.ReadAsync(reader, options, cancellationToken).ConfigureAwait(false),
            _ => throw new VideoDecodeException($"the container '{container}' has no reader")
        };
    }

    internal static string DetectContainer(ReadOnlySpan<byte> header, string extension)
    {
        if (header.Length >= 12 && VideoBinary.StartsWith(header, 0, "RIFF"u8) && VideoBinary.StartsWith(header, 8, "AVI "u8))
        {
            return "avi";
        }

        if (header.Length >= 12 && VideoBinary.StartsWith(header, 4, "ftyp"u8))
        {
            // The major brand separates QuickTime from ISO base media files.
            return header.Length >= 12 && VideoBinary.StartsWith(header, 8, "qt  "u8) ? "mov" : "mp4";
        }

        if (VideoBinary.TryReadEbmlId(header, 0, out var id, out _) && id == 0x1A45DFA3)
        {
            return "matroska";
        }

        return FromExtension(extension);
    }

    private static string FromExtension(string extension) =>
        (extension ?? string.Empty).ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "mp4",
            ".mov" => "mov",
            ".mkv" => "matroska",
            ".webm" => "matroska",
            ".avi" => "avi",
            _ => string.Empty
        };
}
