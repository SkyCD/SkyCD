using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Audio.Exceptions;
using SkyCD.Plugin.Metadata.Audio.Models;

namespace SkyCD.Plugin.Metadata.Audio.Parsing;

/// <summary>
/// Identifies the container from its bytes and hands the source to the TagLibSharp-backed reader.
/// </summary>
/// <remarks>
/// Detection is content-first: the host selects this plugin by extension or MIME type, but a file catalogued
/// with the wrong extension is still read correctly, and one whose bytes match nothing this plugin supports
/// fails with a clear reason rather than being half-parsed by the wrong reader.
/// </remarks>
internal static class AudioProber
{
    public static Task<AudioProbe> ProbeAsync(
        MediaItem item,
        IMediaSource source,
        AudioPluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        return ProbeCoreAsync(item, source, cancellationToken);
    }

    private static async Task<AudioProbe> ProbeCoreAsync(
        MediaItem item,
        IMediaSource source,
        CancellationToken cancellationToken)
    {
        var reader = new AudioSourceReader(source, item);
        var header = await reader.ReadAsync(0, 12, cancellationToken).ConfigureAwait(false);
        var container = DetectContainer(header.Span, item.Extension);
        if (container.Length == 0)
        {
            throw new AudioDecodeException("the bytes match no supported audio container");
        }

        return TagLibAudioProber.Probe(reader, container, cancellationToken);
    }

    internal static string DetectContainer(ReadOnlySpan<byte> header, string extension)
    {
        if (StartsWith(header, 0, "fLaC"u8))
        {
            return "flac";
        }

        if (StartsWith(header, 0, "OggS"u8))
        {
            return "ogg";
        }

        // An MP4 file type box starts at offset 4, after the box size.
        if (header.Length >= 8 && StartsWith(header, 4, "ftyp"u8))
        {
            return "mp4";
        }

        if (StartsWith(header, 0, "ID3"u8))
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

    private static bool StartsWith(ReadOnlySpan<byte> data, int offset, ReadOnlySpan<byte> marker) =>
        offset < data.Length && data[offset..].StartsWith(marker);

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
