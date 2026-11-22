using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Video.Exceptions;
using SkyCD.Plugin.Metadata.Video.Models;

namespace SkyCD.Plugin.Metadata.Video.Parsing;

/// <summary>
/// Reads a Matroska or WebM source by walking its EBML element tree.
/// </summary>
/// <remarks>
/// Only element headers are read and payloads are skipped by offset, so the clusters that make up almost all
/// of a file's size are never touched. Segment elements may declare an unknown size, which means "runs to the
/// end of the parent", and the walker honours that instead of failing.
/// </remarks>
internal static class MatroskaReader
{
    private const int MaxDepth = 10;
    private const int MaxElements = 4096;
    private const int MaxLeafRead = 256;

    // Element ids from the Matroska specification.
    private const uint IdEbmlHeader = 0x1A45DFA3;
    private const uint IdDocType = 0x4282;
    private const uint IdSegment = 0x18538067;
    private const uint IdInfo = 0x1549A966;
    private const uint IdTimecodeScale = 0x2AD7B1;
    private const uint IdDuration = 0x4489;
    private const uint IdTitle = 0x7BA9;
    private const uint IdTracks = 0x1654AE6B;
    private const uint IdTrackEntry = 0xAE;
    private const uint IdTrackType = 0x83;
    private const uint IdCodecId = 0x86;
    private const uint IdDefaultDuration = 0x23E383;
    private const uint IdVideo = 0xE0;
    private const uint IdAudio = 0xE1;
    private const uint IdPixelWidth = 0xB0;
    private const uint IdPixelHeight = 0xBA;
    private const uint IdDisplayWidth = 0x54B0;
    private const uint IdDisplayHeight = 0x54BA;
    private const uint IdProjection = 0x7670;
    private const uint IdProjectionPoseRoll = 0x76BA;
    private const uint IdChannels = 0x9F;

    private static readonly HashSet<uint> ContainerIds =
    [
        IdEbmlHeader, IdSegment, IdInfo, IdTracks, IdTrackEntry, IdVideo, IdAudio, IdProjection
    ];

    public static async Task<VideoProbe> ReadAsync(
        VideoSourceReader source,
        VideoPluginOptions options,
        CancellationToken cancellationToken)
    {
        var header = await source.ReadBytesAsync(0, 64, cancellationToken).ConfigureAwait(false);
        if (!VideoBinary.TryReadEbmlId(header, 0, out var rootId, out var rootIdLength) || rootId != IdEbmlHeader)
        {
            throw new VideoDecodeException("the source does not start with an EBML header");
        }

        var state = new WalkState();
        await WalkAsync(source, 0, source.Length, 0, state, cancellationToken).ConfigureAwait(false);

        if (state.DocType is null)
        {
            throw new VideoDecodeException("the EBML header does not declare a Matroska or WebM document type");
        }

        if (!state.SawSegment)
        {
            throw new VideoDecodeException("the Matroska file has no segment");
        }

        // The final track has no following TrackEntry to trigger its commit.
        state.CommitTrack();

        // Duration is stored in timecode units, and the scale may appear after the duration element.
        if (state.DurationTimecodes is { } timecodes && state.TimecodeScaleNanoseconds > 0)
        {
            state.DurationSeconds = timecodes * state.TimecodeScaleNanoseconds / 1_000_000_000.0;
        }

        var probe = new VideoProbe
        {
            Container = state.DocType,
            DurationSeconds = state.DurationSeconds,
            Width = state.DisplayWidth ?? state.PixelWidth,
            Height = state.DisplayHeight ?? state.PixelHeight,
            RotationDegrees = state.RotationDegrees,
            VideoCodec = state.VideoCodec,
            AudioCodec = state.AudioCodec,
            FrameRate = state.FrameRate,
            Title = state.Title
        };

        probe.Streams.Add(new VideoStreamInfo("video", state.VideoCodec, state.FrameRate, probe.Width, probe.Height, null));
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
        int depth,
        WalkState state,
        CancellationToken cancellationToken)
    {
        if (depth > MaxDepth)
        {
            return;
        }

        var position = start;
        while (position < end)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++state.ElementsVisited > MaxElements)
            {
                return;
            }

            // Up to four id bytes plus eight size bytes.
            var head = await source.ReadBytesAsync(position, 12, cancellationToken).ConfigureAwait(false);
            if (head.Length < 2 ||
                !VideoBinary.TryReadEbmlId(head, 0, out var id, out var idLength) ||
                !VideoBinary.TryReadEbmlSize(head, idLength, out var size, out var sizeLength))
            {
                return;
            }

            var payloadStart = position + idLength + sizeLength;
            var payloadEnd = size < 0 ? end : payloadStart + size;
            if (payloadEnd > end)
            {
                payloadEnd = end;
            }

            await VisitAsync(source, id, head, idLength + sizeLength, payloadStart, payloadEnd, depth, state, cancellationToken)
                .ConfigureAwait(false);

            position = payloadEnd;
        }
    }

    private static async Task VisitAsync(
        VideoSourceReader source,
        uint id,
        byte[] head,
        int headerLength,
        long payloadStart,
        long payloadEnd,
        int depth,
        WalkState state,
        CancellationToken cancellationToken)
    {
        if (id == IdSegment)
        {
            state.SawSegment = true;
            await WalkAsync(source, payloadStart, payloadEnd, depth + 1, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (id == IdTrackEntry)
        {
            state.CommitTrack();
            state.BeginTrack();
        }

        if (ContainerIds.Contains(id))
        {
            await WalkAsync(source, payloadStart, payloadEnd, depth + 1, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        var length = (int)Math.Min(payloadEnd - payloadStart, MaxLeafRead);
        if (length <= 0)
        {
            return;
        }

        // The header bytes already fetched carry small leaves, so avoid re-reading them.
        var inlineLength = Math.Min(length, Math.Max(0, head.Length - headerLength));
        byte[] body;
        if (inlineLength >= length)
        {
            body = new byte[length];
            Array.Copy(head, headerLength, body, 0, length);
        }
        else
        {
            body = await source.ReadBytesAsync(payloadStart, length, cancellationToken).ConfigureAwait(false);
        }

        if (body.Length == 0)
        {
            return;
        }

        switch (id)
        {
            case IdDocType:
                state.DocType = NormalizeDocType(VideoBinary.ReadEbmlString(body, 0, body.Length));
                break;
            case IdTimecodeScale:
                if (VideoBinary.TryReadEbmlUInt(body, 0, body.Length, out var scale) && scale > 0)
                {
                    state.TimecodeScaleNanoseconds = scale;
                }

                break;
            case IdDuration:
                if (VideoBinary.TryReadEbmlFloat(body, 0, body.Length, out var duration) && duration > 0)
                {
                    state.DurationTimecodes = duration;
                }

                break;
            case IdTitle:
                state.Title ??= NullIfEmpty(VideoBinary.ReadEbmlString(body, 0, body.Length));
                break;
            case IdTrackType:
                if (VideoBinary.TryReadEbmlUInt(body, 0, body.Length, out var trackType))
                {
                    state.PendingTrackType = (int)trackType;
                }

                break;
            case IdCodecId:
                var codecId = NullIfEmpty(VideoBinary.ReadEbmlString(body, 0, body.Length));
                if (codecId is not null)
                {
                    state.PendingCodec ??= NormalizeCodecId(codecId);
                }

                break;
            case IdDefaultDuration:
                if (VideoBinary.TryReadEbmlUInt(body, 0, body.Length, out var nanoseconds) && nanoseconds > 0)
                {
                    state.PendingFrameRate = Math.Round(1_000_000_000.0 / nanoseconds, 3);
                }

                break;
            case IdPixelWidth:
                if (VideoBinary.TryReadEbmlUInt(body, 0, body.Length, out var pixelWidth) && pixelWidth > 0)
                {
                    state.PendingPixelWidth = (int)pixelWidth;
                }

                break;
            case IdPixelHeight:
                if (VideoBinary.TryReadEbmlUInt(body, 0, body.Length, out var pixelHeight) && pixelHeight > 0)
                {
                    state.PendingPixelHeight = (int)pixelHeight;
                }

                break;
            case IdDisplayWidth:
                if (VideoBinary.TryReadEbmlUInt(body, 0, body.Length, out var displayWidth) && displayWidth > 0)
                {
                    state.PendingDisplayWidth = (int)displayWidth;
                }

                break;
            case IdDisplayHeight:
                if (VideoBinary.TryReadEbmlUInt(body, 0, body.Length, out var displayHeight) && displayHeight > 0)
                {
                    state.PendingDisplayHeight = (int)displayHeight;
                }

                break;
            case IdProjectionPoseRoll:
                if (VideoBinary.TryReadEbmlFloat(body, 0, body.Length, out var roll))
                {
                    state.PendingRotationDegrees = NormalizeRotation(roll);
                }

                break;
            case IdChannels:
                if (VideoBinary.TryReadEbmlUInt(body, 0, body.Length, out var channels) && channels > 0)
                {
                    state.PendingChannels = (int)channels;
                }

                break;
        }
    }

    /// <summary>
    /// Maps a Matroska codec identifier onto the short name the schema publishes. The identifier is already
    /// stable, so an unknown one passes through lowercased rather than being dropped.
    /// </summary>
    internal static string NormalizeCodecId(string codecId) => codecId switch
    {
        "V_MPEG4/ISO/AVC" => "h264",
        "V_MPEGH/ISO/HEVC" => "hevc",
        "V_MPEG4/ISO/AP" => "mpeg4-part2",
        "V_MPEG2" => "mpeg2",
        "V_VP8" => "vp8",
        "V_VP9" => "vp9",
        "V_AV1" => "av1",
        "V_MJPEG" => "mjpeg",
        "V_THEORA" => "theora",
        "A_AAC" or "A_AAC/MPEG4/LC" => "aac",
        "A_AC3" => "ac3",
        "A_EAC3" => "eac3",
        "A_DTS" => "dts",
        "A_MPEG/L3" => "mp3",
        "A_OPUS" or "A_OPUS/48000" => "opus",
        "A_VORBIS" => "vorbis",
        "A_FLAC" => "flac",
        "A_ALAC" => "alac",
        "A_PCM/INT/LIT" => "pcm",
        _ => codecId.TrimStart('V', 'A', '_').ToLowerInvariant()
    };

    private static string? NormalizeDocType(string? docType) =>
        string.IsNullOrWhiteSpace(docType)
            ? null
            : docType.Trim().ToLowerInvariant() switch
            {
                "matroska" => "mkv",
                "webm" => "webm",
                var other => other
            };

    /// <summary>Matroska stores roll as a signed float; the schema publishes clockwise degrees 0..359.</summary>
    internal static int NormalizeRotation(double roll)
    {
        var degrees = (int)Math.Round(roll) % 360;
        if (degrees < 0)
        {
            degrees += 360;
        }

        return degrees;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Mutable state for one walk. Track-scoped facts are collected as pending values and only committed once
    /// the track's type is known, because Matroska does not require <c>TrackType</c> to precede the video or
    /// audio element it describes.
    /// </summary>
    private sealed class WalkState
    {
        public int ElementsVisited { get; set; }
        public bool SawSegment { get; set; }
        public string? DocType { get; set; }
        public ulong TimecodeScaleNanoseconds { get; set; } = 1_000_000;
        public double? DurationTimecodes { get; set; }
        public double? DurationSeconds { get; set; }
        public string? Title { get; set; }

        public string? VideoCodec { get; set; }
        public string? AudioCodec { get; set; }
        public double? FrameRate { get; set; }
        public int? PixelWidth { get; set; }
        public int? PixelHeight { get; set; }
        public int? DisplayWidth { get; set; }
        public int? DisplayHeight { get; set; }
        public int? RotationDegrees { get; set; }
        public int? Channels { get; set; }

        public int PendingTrackType { get; set; }
        public string? PendingCodec { get; set; }
        public double? PendingFrameRate { get; set; }
        public int? PendingPixelWidth { get; set; }
        public int? PendingPixelHeight { get; set; }
        public int? PendingDisplayWidth { get; set; }
        public int? PendingDisplayHeight { get; set; }
        public int? PendingRotationDegrees { get; set; }
        public int? PendingChannels { get; set; }

        public void BeginTrack()
        {
            PendingTrackType = 0;
            PendingCodec = null;
            PendingFrameRate = null;
            PendingPixelWidth = null;
            PendingPixelHeight = null;
            PendingDisplayWidth = null;
            PendingDisplayHeight = null;
            PendingRotationDegrees = null;
            PendingChannels = null;
        }

        /// <summary>Folds the pending track into the resolved fields if it is a video or audio track.</summary>
        public void CommitTrack()
        {
            if (PendingTrackType == 1)
            {
                VideoCodec ??= PendingCodec;
                FrameRate ??= PendingFrameRate;
                PixelWidth ??= PendingPixelWidth;
                PixelHeight ??= PendingPixelHeight;
                DisplayWidth ??= PendingDisplayWidth;
                DisplayHeight ??= PendingDisplayHeight;
                RotationDegrees ??= PendingRotationDegrees;
            }
            else if (PendingTrackType == 2)
            {
                AudioCodec ??= PendingCodec;
                Channels ??= PendingChannels;
            }

            BeginTrack();
        }
    }
}
