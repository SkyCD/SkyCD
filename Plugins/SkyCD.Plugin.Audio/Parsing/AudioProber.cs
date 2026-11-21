using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Audio.Exceptions;
using SkyCD.Plugin.Audio.Models;

namespace SkyCD.Plugin.Audio.Parsing;

/// <summary>
/// Identifies the container from its bytes and dispatches to the matching reader.
/// </summary>
/// <remarks>
/// Detection is content-first: the host selects this plugin by extension or MIME type, but a file catalogued
/// with the wrong extension is still read correctly, and one whose bytes match nothing this plugin supports
/// fails with a clear reason rather than being half-parsed by the wrong reader.
/// </remarks>
internal static class AudioProber
{
    public static async Task<AudioProbe> ProbeAsync(
        MediaItem item,
        IMediaSource source,
        AudioPluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var reader = new AudioSourceReader(source, item);
        var header = await reader.ReadAsync(0, 12, cancellationToken).ConfigureAwait(false);
        var container = DetectContainer(header.Span, item.Extension);

        return container switch
        {
            "mp3" => await MpegReader.ReadAsync(reader, options, cancellationToken).ConfigureAwait(false),
            "flac" => await FlacReader.ReadAsync(reader, options, cancellationToken).ConfigureAwait(false),
            "ogg" => await OggReader.ReadAsync(reader, options, cancellationToken).ConfigureAwait(false),
            "mp4" => await Mp4Reader.ReadAsync(reader, options, cancellationToken).ConfigureAwait(false),
            _ => throw new AudioDecodeException($"the container '{container}' has no reader")
        };
    }

    internal static string DetectContainer(ReadOnlySpan<byte> header, string extension)
    {
        if (AudioBinary.StartsWith(header, 0, "fLaC"u8))
        {
            return "flac";
        }

        if (AudioBinary.StartsWith(header, 0, "OggS"u8))
        {
            return "ogg";
        }

        // An MP4 file type box starts at offset 4, after the box size.
        if (header.Length >= 8 && AudioBinary.StartsWith(header, 4, "ftyp"u8))
        {
            return "mp4";
        }

        if (AudioBinary.StartsWith(header, 0, "ID3"u8))
        {
            return "mp3";
        }

        // A bare MPEG frame: eleven sync bits.
        if (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
        {
            return "mp3";
        }

        return FromExtension(extension);
    }

    private static string FromExtension(string extension) =>
        (extension ?? string.Empty).ToLowerInvariant() switch
        {
            ".mp3" => "mp3",
            ".flac" => "flac",
            ".ogg" or ".oga" => "ogg",
            ".m4a" or ".m4b" => "mp4",
            _ => string.Empty
        };
}
