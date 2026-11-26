using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MetadataExtractor.Formats.Avi;
using SkyCD.Plugin.Metadata.Video.Exceptions;
using SkyCD.Plugin.Metadata.Video.Models;

namespace SkyCD.Plugin.Metadata.Video.Parsing;

/// <summary>
/// Reads a RIFF/AVI source: the main header for size and frame count, and each stream header for codec,
/// rate and format details.
/// </summary>
/// <remarks>
/// The <c>movi</c> and <c>idx1</c> chunks that make up almost all of the file are skipped by offset, so only
/// the <c>hdrl</c> list is ever read. AVI has no standard rotation field, so rotation is left absent rather
/// than guessed.
/// </remarks>
internal static class AviReader
{
    private const int MaxChunks = 1024;
    private const int StreamHeaderLength = 56;

    public static async Task<VideoProbe> ReadAsync(
        VideoSourceReader source,
        VideoPluginOptions options,
        CancellationToken cancellationToken)
    {
        var header = await source.ReadBytesAsync(0, 12, cancellationToken).ConfigureAwait(false);
        if (header.Length < 12 ||
            !VideoBinary.StartsWith(header, 0, "RIFF"u8) ||
            !VideoBinary.StartsWith(header, 8, "AVI "u8))
        {
            throw new VideoDecodeException("the source is not a RIFF/AVI file");
        }

        var state = new WalkState();
        await WalkAsync(source, 12, source.Length, state, cancellationToken).ConfigureAwait(false);
        await ApplyMetadataExtractorAsync(source, options, state, cancellationToken).ConfigureAwait(false);

        if (!state.SawMainHeader && state.VideoCodec is null)
        {
            throw new VideoDecodeException("the AVI file has no header list");
        }

        state.ResolveDuration();

        var probe = new VideoProbe
        {
            Container = "avi",
            DurationSeconds = state.DurationSeconds,
            Width = state.Width,
            Height = state.Height,
            VideoCodec = state.VideoCodec,
            AudioCodec = state.AudioCodec,
            FrameRate = state.FrameRate
        };

        probe.Streams.Add(new VideoStreamInfo("video", state.VideoCodec, state.FrameRate, state.Width, state.Height, null));
        if (state.AudioCodec is not null)
        {
            probe.Streams.Add(new VideoStreamInfo("audio", state.AudioCodec, null, null, null, state.Channels));
        }

        if (probe.DurationSeconds is > 0)
        {
            probe.BitrateKbps = (int)Math.Round(source.Length * 8.0 / probe.DurationSeconds.Value / 1000.0);
        }

        _ = options;
        return probe;
    }

    private static async Task WalkAsync(
        VideoSourceReader source,
        long start,
        long end,
        WalkState state,
        CancellationToken cancellationToken)
    {
        var position = start;
        while (position + 8 <= end)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++state.ChunksVisited > MaxChunks)
            {
                return;
            }

            var header = await source.ReadBytesAsync(position, 8, cancellationToken).ConfigureAwait(false);
            if (header.Length < 8 || !VideoBinary.TryU32LE(header, 4, out var size))
            {
                throw new VideoDecodeException("the AVI chunk is truncated");
            }

            var fourCc = Encoding.ASCII.GetString(header, 0, 4);
            var payloadStart = position + 8;

            // Chunks are word aligned; an odd size carries one padding byte.
            // A declared size past the source is a truncated file. Clamping it used to let a
            // 24-byte AVI header look like a readable container.
            var payloadEnd = payloadStart + size + (size % 2 == 1 ? 1 : 0);
            if (payloadEnd > end)
            {
                throw new VideoDecodeException("the AVI chunk is truncated");
            }

            if (fourCc == "LIST")
            {
                var listHeader = await source.ReadBytesAsync(payloadStart, 4, cancellationToken).ConfigureAwait(false);
                var listType = listHeader.Length == 4 ? Encoding.ASCII.GetString(listHeader, 0, 4) : string.Empty;

                if (listType is "hdrl" or "strl")
                {
                    if (listType == "hdrl")
                    {
                        if (state.HdrlStart is null)
                        {
                            state.HdrlStart = position;
                            state.HdrlEnd = payloadEnd;
                        }
                    }
                    else
                    {
                        state.BeginStream();
                    }

                    await WalkAsync(source, payloadStart + 4, payloadEnd, state, cancellationToken).ConfigureAwait(false);
                }
            }
            else if (fourCc == "strh")
            {
                await ReadStreamHeaderAsync(source, payloadStart, payloadEnd, state, cancellationToken).ConfigureAwait(false);
            }
            else if (fourCc == "strf")
            {
                await ReadStreamFormatAsync(source, payloadStart, payloadEnd, state, cancellationToken).ConfigureAwait(false);
            }

            position = payloadEnd;
        }
    }

    /// <summary>
    /// Reads the header list with MetadataExtractor and maps its AVI directory onto the probe.
    /// Dimensions, frame rate, duration and the video codec come from the parsed RIFF header;
    /// stream kinds stay with the hand reader below because MetadataExtractor does not report
    /// which streams are video and which are audio. Anything beyond the options' header-scan
    /// budget is left unread so a huge header can never cost more than the walk already would.
    /// </summary>
    private static async Task ApplyMetadataExtractorAsync(
        VideoSourceReader source,
        VideoPluginOptions options,
        WalkState state,
        CancellationToken cancellationToken)
    {
        if (state.HdrlStart is not { } hdrlStart || state.HdrlEnd is not { } hdrlEnd)
        {
            return;
        }

        var hdrlLength = hdrlEnd - hdrlStart;
        if (hdrlLength <= 0 || hdrlLength > options.HeaderScanBytes)
        {
            return;
        }

        var hdrl = await source.ReadBytesAsync(hdrlStart, (int)hdrlLength, cancellationToken).ConfigureAwait(false);
        if (hdrl.Length != hdrlLength)
        {
            return;
        }

        // MetadataExtractor needs the RIFF envelope, so reattach the validated 12-byte header
        // the walk already consumed. The size field covers the header list plus the form type.
        var riff = new byte[12 + hdrl.Length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(riff, 0);
        BitConverter.GetBytes(hdrl.Length + 4).CopyTo(riff, 4);
        Encoding.ASCII.GetBytes("AVI ").CopyTo(riff, 8);
        hdrl.CopyTo(riff, 12);

        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = AviMetadataReader.ReadMetadata(new MemoryStream(riff));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return;
        }

        foreach (var directory in directories)
        {
            if (directory is not AviDirectory avi)
            {
                continue;
            }

            state.SawMainHeader = true;
            if (VideoBinary.TryToInt(avi.GetObject(AviDirectory.TagWidth), out var width) && width > 0 && width <= 32767)
            {
                state.Width ??= width;
            }

            if (VideoBinary.TryToInt(avi.GetObject(AviDirectory.TagHeight), out var height) && height > 0 && height <= 32767)
            {
                state.Height ??= height;
            }

            if (VideoBinary.TryToDouble(avi.GetObject(AviDirectory.TagFramesPerSecond), out var framesPerSecond) && framesPerSecond > 0)
            {
                state.FrameRate ??= Math.Round(framesPerSecond, 3);
            }

            if (state.VideoCodec is null && avi.GetObject(AviDirectory.TagVideoCodec) is string codec && codec.Length > 0)
            {
                state.VideoCodec = ResolveVideoCodec(codec);
            }

            if (state.MeDurationSeconds is null && VideoBinary.TryToDuration(avi.GetObject(AviDirectory.TagDuration), out var duration))
            {
                state.MeDurationSeconds = duration.TotalSeconds;
            }
        }
    }

    private static async Task ReadStreamHeaderAsync(
        VideoSourceReader source,
        long payloadStart,
        long payloadEnd,
        WalkState state,
        CancellationToken cancellationToken)
    {
        var length = (int)Math.Min(StreamHeaderLength, payloadEnd - payloadStart);
        var payload = await source.ReadBytesAsync(payloadStart, length, cancellationToken).ConfigureAwait(false);
        if (payload.Length < 32)
        {
            return;
        }

        var streamType = Encoding.ASCII.GetString(payload, 0, 4);
        if (streamType == "vids")
        {
            state.StreamKind = "video";
        }
        else if (streamType == "auds")
        {
            state.StreamKind = "audio";
            state.AudioHandler = Encoding.ASCII.GetString(payload, 4, 4);
        }
        else
        {
            state.StreamKind = null;
            return;
        }

        // The video stream's own length and rate stay hand-read: they select the stream kind that
        // MetadataExtractor cannot distinguish, and they feed the duration preference below.
        if (state.StreamKind == "video" &&
            VideoBinary.TryU32LE(payload, 20, out var scale) &&
            VideoBinary.TryU32LE(payload, 24, out var rate) &&
            scale > 0 && rate > 0 &&
            VideoBinary.TryU32LE(payload, 32, out var streamLength) && streamLength > 0)
        {
            state.StreamDurationSeconds = streamLength * scale / (double)rate;
        }
    }

    private static async Task ReadStreamFormatAsync(
        VideoSourceReader source,
        long payloadStart,
        long payloadEnd,
        WalkState state,
        CancellationToken cancellationToken)
    {
        var length = (int)Math.Min(40, payloadEnd - payloadStart);
        var payload = await source.ReadBytesAsync(payloadStart, length, cancellationToken).ConfigureAwait(false);

        if (state.StreamKind == "audio" && payload.Length >= 18)
        {
            // WAVEFORMATEX: format tag, channels, sample rate, average bytes per second, block align, bits.
            if (VideoBinary.TryU16LE(payload, 0, out var formatTag))
            {
                state.AudioCodec ??= ResolveAudioFormatTag(formatTag, state.AudioHandler);
            }

            if (VideoBinary.TryU16LE(payload, 2, out var channels) && channels > 0)
            {
                state.Channels = channels;
            }
        }
    }

    internal static string ResolveVideoCodec(string handler)
    {
        var normalized = (handler ?? string.Empty).Trim();
        return normalized.ToUpperInvariant() switch
        {
            "" or "0" => "raw",
            "H264" or "AVC1" or "X264" => "h264",
            "H265" or "HEVC" or "HEV1" or "HVC1" => "hevc",
            "XVID" or "DIVX" or "DX50" or "MP4V" or "MPG4" => "mpeg4",
            "MPG1" or "MPG2" or "MPEG" => "mpeg2",
            "MJPG" or "JPEG" => "mjpeg",
            "VP80" or "VP8" => "vp8",
            "VP90" or "VP9" => "vp9",
            "AV01" or "AV1" => "av1",
            "HFYU" => "huffyuv",
            "FFV1" => "ffv1",
            "I420" or "YV12" or "IYUV" => "raw-yuv",
            _ => normalized.ToLowerInvariant()
        };
    }

    private static string ResolveAudioFormatTag(ushort formatTag, string? handler) => formatTag switch
    {
        0x0001 => "pcm",
        0x0003 => "pcm-float",
        0x0055 => "mp3",
        0x00FF => "aac",
        0x2000 => "ac3",
        0x2001 => "dts",
        0x0161 or 0x0162 or 0x0163 => "wma",
        _ => string.IsNullOrWhiteSpace(handler)
            ? $"0x{formatTag:X4}".ToLowerInvariant()
            : handler!.Trim().ToLowerInvariant()
    };

    private sealed class WalkState
    {
        public int ChunksVisited { get; set; }
        public bool SawMainHeader { get; set; }
        public string? StreamKind { get; set; }
        public string? AudioHandler { get; set; }
        public long? HdrlStart { get; set; }
        public long? HdrlEnd { get; set; }
        public double? StreamDurationSeconds { get; set; }
        public double? MeDurationSeconds { get; set; }
        public double? DurationSeconds { get; set; }
        public double? FrameRate { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? VideoCodec { get; set; }
        public string? AudioCodec { get; set; }
        public int? Channels { get; set; }

        public void BeginStream()
        {
            StreamKind = null;
            AudioHandler = null;
        }

        /// <summary>
        /// Prefers the video stream's own sample count and rate, which stay correct when the main header's
        /// frame count was rounded, and falls back to the MetadataExtractor duration.
        /// </summary>
        public void ResolveDuration()
        {
            if (StreamDurationSeconds is > 0)
            {
                DurationSeconds = StreamDurationSeconds;
                return;
            }

            if (MeDurationSeconds is > 0)
            {
                DurationSeconds = MeDurationSeconds;
            }
        }
    }
}
