using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Video.Exceptions;
using SkyCD.Plugin.Metadata.Video.Models;

namespace SkyCD.Plugin.Metadata.Video.Parsing;

/// <summary>
/// Reads an MP4 / QuickTime source by walking the atom tree. Only headers are read and payloads are skipped
/// by offset, so a two-gigabyte file costs a few kilobytes of the host's read budget — including the common
/// case where the <c>moov</c> box sits at the end.
/// </summary>
internal static class Mp4VideoReader
{
    private const int MaxDepth = 12;
    private const int MaxAtoms = 4096;

    public static async Task<VideoProbe> ReadAsync(
        VideoSourceReader source,
        VideoPluginOptions options,
        CancellationToken cancellationToken)
    {
        var header = await source.ReadBytesAsync(0, 12, cancellationToken).ConfigureAwait(false);
        if (header.Length < 12 || !VideoBinary.StartsWith(header, 4, "ftyp"u8))
        {
            throw new VideoDecodeException("the source does not start with an MP4 file type box");
        }

        var state = new WalkState(options)
        {
            // The major brand separates QuickTime from ISO base media files.
            IsQuickTime = VideoBinary.StartsWith(header, 8, "qt  "u8)
        };

        await WalkAsync(source, 0, source.Length, 0, state, cancellationToken).ConfigureAwait(false);

        if (!state.SawMovieHeader && state.Codec is null)
        {
            // An ftyp box alone is not a video file; a truncated or audio-only MP4 must not read as empty tags.
            throw new VideoDecodeException("the MP4 file has no movie header and no video sample entry");
        }

        var duration = state.MovieDurationSeconds ?? state.TrackDurationSeconds;
        var probe = new VideoProbe
        {
            Container = state.IsQuickTime ? "mov" : "mp4",
            DurationSeconds = duration,
            Width = state.Width,
            Height = state.Height,
            RotationDegrees = state.RotationDegrees,
            VideoCodec = state.Codec,
            AudioCodec = state.AudioCodec,
            FrameRate = state.FrameRate,
            Title = state.Title
        };

        for (var i = 0; i < state.VideoStreamCount; i++)
        {
            probe.Streams.Add(new VideoStreamInfo(
                "video", state.Codec, state.FrameRate, state.Width, state.Height, null));
        }

        for (var i = 0; i < state.AudioStreamCount; i++)
        {
            probe.Streams.Add(new VideoStreamInfo("audio", state.AudioCodec, null, null, null, null));
        }

        if (duration is > 0)
        {
            probe.BitrateKbps = (int)Math.Round(source.Length * 8.0 / duration.Value / 1000.0);
        }

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
        while (position + 8 <= end)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++state.AtomsVisited > MaxAtoms)
            {
                return;
            }

            var header = await source.ReadBytesAsync(position, 8, cancellationToken).ConfigureAwait(false);
            if (header.Length < 8 || !VideoBinary.TryU32BE(header, 0, out var size32))
            {
                return;
            }

            var type = Encoding.ASCII.GetString(header, 4, 4);
            long headerLength = 8;
            long atomSize = size32;

            if (size32 == 1)
            {
                var largesize = await source.ReadBytesAsync(position + 8, 8, cancellationToken).ConfigureAwait(false);
                if (!VideoBinary.TryU64BE(largesize, 0, out var size64))
                {
                    return;
                }

                atomSize = (long)size64;
                headerLength = 16;
            }
            else if (size32 == 0)
            {
                // A zero size means the atom runs to the end of its parent.
                atomSize = end - position;
            }

            if (atomSize < headerLength || position + atomSize > end)
            {
                return;
            }

            var payloadStart = position + headerLength;
            var payloadEnd = position + atomSize;

            await VisitAsync(source, type, payloadStart, payloadEnd, depth, state, cancellationToken)
                .ConfigureAwait(false);

            position = payloadEnd;
        }
    }

    private static async Task VisitAsync(
        VideoSourceReader source,
        string type,
        long payloadStart,
        long payloadEnd,
        int depth,
        WalkState state,
        CancellationToken cancellationToken)
    {
        switch (type)
        {
            case "moov":
            case "trak":
            case "mdia":
            case "minf":
            case "stbl":
            case "udta":
            case "edts":
                if (type == "trak")
                {
                    state.BeginTrack();
                }

                await WalkAsync(source, payloadStart, payloadEnd, depth + 1, state, cancellationToken)
                    .ConfigureAwait(false);
                return;

            case "meta":
                // A full box: four bytes of version and flags precede its children.
                await WalkAsync(source, payloadStart + 4, payloadEnd, depth + 1, state, cancellationToken)
                    .ConfigureAwait(false);
                return;

            case "ilst":
                await ReadItemListAsync(source, payloadStart, payloadEnd, state, cancellationToken).ConfigureAwait(false);
                return;

            case "mvhd":
                await ReadMovieHeaderAsync(source, payloadStart, state, cancellationToken).ConfigureAwait(false);
                return;

            case "tkhd":
                await ReadTrackHeaderAsync(source, payloadStart, payloadEnd, state, cancellationToken)
                    .ConfigureAwait(false);
                return;

            case "mdhd":
                await ReadMediaHeaderAsync(source, payloadStart, state, cancellationToken).ConfigureAwait(false);
                return;

            case "hdlr":
                await ReadHandlerAsync(source, payloadStart, state, cancellationToken).ConfigureAwait(false);
                return;

            case "stsd":
                await ReadSampleDescriptionAsync(source, payloadStart, payloadEnd, state, cancellationToken)
                    .ConfigureAwait(false);
                return;

            case "stts":
                await ReadTimeToSampleAsync(source, payloadStart, state, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    private static async Task ReadMovieHeaderAsync(
        VideoSourceReader source,
        long payloadStart,
        WalkState state,
        CancellationToken cancellationToken)
    {
        var payload = await source.ReadBytesAsync(payloadStart, 32, cancellationToken).ConfigureAwait(false);
        if (payload.Length < 20)
        {
            return;
        }

        var version = payload[0];
        uint timescale;
        ulong duration;

        if (version == 1)
        {
            if (!VideoBinary.TryU32BE(payload, 20, out timescale) ||
                !VideoBinary.TryU64BE(payload, 24, out duration))
            {
                return;
            }
        }
        else
        {
            if (!VideoBinary.TryU32BE(payload, 12, out timescale) ||
                !VideoBinary.TryU32BE(payload, 16, out var duration32))
            {
                return;
            }

            duration = duration32;
        }

        state.SawMovieHeader = true;
        if (timescale > 0 && duration > 0)
        {
            state.MovieDurationSeconds = (double)duration / timescale;
        }
    }

    /// <summary>
    /// Reads the track header, which carries both the display size and the transformation matrix that encodes
    /// rotation. The matrix is the authoritative source: width and height are stored unrotated.
    /// </summary>
    private static async Task ReadTrackHeaderAsync(
        VideoSourceReader source,
        long payloadStart,
        long payloadEnd,
        WalkState state,
        CancellationToken cancellationToken)
    {
        var payload = await source.ReadBytesAsync(payloadStart, (int)Math.Min(92, payloadEnd - payloadStart), cancellationToken)
            .ConfigureAwait(false);
        if (payload.Length < 8)
        {
            return;
        }

        var version = payload[0];
        var matrixOffset = version == 1 ? 48 : 40;
        var sizeOffset = matrixOffset + 36;

        // The track header precedes the handler that identifies the track, so size and rotation are held
        // pending and only committed once the track is known to carry video.
        if (state.PendingRotationDegrees is null && payload.Length > matrixOffset + 20)
        {
            state.PendingRotationDegrees = RotationFromMatrix(payload, matrixOffset);
        }

        if (payload.Length >= sizeOffset + 8 &&
            VideoBinary.TryU32BE(payload, sizeOffset, out var widthFixed) &&
            VideoBinary.TryU32BE(payload, sizeOffset + 4, out var heightFixed))
        {
            var width = (int)(widthFixed >> 16);
            var height = (int)(heightFixed >> 16);
            if (width > 0 && height > 0 && state.PendingWidth is null)
            {
                state.PendingWidth = width;
                state.PendingHeight = height;
            }
        }
    }

    /// <summary>
    /// Derives clockwise rotation from the 16.16 fixed-point matrix. The matrix is stored row-major as
    /// a, b, u, c, d, v, x, y, w, so <c>c</c> and <c>d</c> sit at offsets 12 and 16 rather than 8 and 12.
    /// Only the four axis-aligned rotations are recognized; an arbitrary transform is reported as upright
    /// rather than guessed at.
    /// </summary>
    internal static int RotationFromMatrix(ReadOnlySpan<byte> matrix, int offset)
    {
        if (offset + 20 > matrix.Length)
        {
            return 0;
        }

        var a = ReadFixed16(matrix, offset);
        var b = ReadFixed16(matrix, offset + 4);
        var c = ReadFixed16(matrix, offset + 12);
        var d = ReadFixed16(matrix, offset + 16);

        if (a == 1 && b == 0 && c == 0 && d == 1)
        {
            return 0;
        }

        if (a == 0 && b == 1 && c == -1 && d == 0)
        {
            return 90;
        }

        if (a == -1 && b == 0 && c == 0 && d == -1)
        {
            return 180;
        }

        if (a == 0 && b == -1 && c == 1 && d == 0)
        {
            return 270;
        }

        return 0;
    }

    private static int ReadFixed16(ReadOnlySpan<byte> data, int offset)
    {
        if (!VideoBinary.TryU32BE(data, offset, out var raw))
        {
            return 0;
        }

        // The 16.16 value is signed; compare against 65536 rather than converting to float.
        return unchecked((int)raw) / 65536;
    }

    private static async Task ReadMediaHeaderAsync(
        VideoSourceReader source,
        long payloadStart,
        WalkState state,
        CancellationToken cancellationToken)
    {
        var payload = await source.ReadBytesAsync(payloadStart, 32, cancellationToken).ConfigureAwait(false);
        if (payload.Length < 20)
        {
            return;
        }

        var version = payload[0];
        uint timescale;
        ulong duration;

        if (version == 1)
        {
            if (!VideoBinary.TryU32BE(payload, 20, out timescale) ||
                !VideoBinary.TryU64BE(payload, 24, out duration))
            {
                return;
            }
        }
        else
        {
            if (!VideoBinary.TryU32BE(payload, 12, out timescale) ||
                !VideoBinary.TryU32BE(payload, 16, out var duration32))
            {
                return;
            }

            duration = duration32;
        }

        state.TrackTimescale = timescale;
        if (timescale > 0 && duration > 0 && state.TrackDurationSeconds is null)
        {
            state.TrackDurationSeconds = (double)duration / timescale;
        }
    }

    private static async Task ReadHandlerAsync(
        VideoSourceReader source,
        long payloadStart,
        WalkState state,
        CancellationToken cancellationToken)
    {
        // hdlr: version and flags (4), pre-defined (4), handler type (4).
        var payload = await source.ReadBytesAsync(payloadStart + 8, 4, cancellationToken).ConfigureAwait(false);
        if (payload.Length < 4)
        {
            return;
        }

        state.Handler = Encoding.ASCII.GetString(payload, 0, 4);
        if (state.Handler == "vide")
        {
            state.IsVideoTrack = true;
            state.VideoStreamCount++;

            // Commit what the track header held pending, now that this track is known to carry video.
            state.Width ??= state.PendingWidth;
            state.Height ??= state.PendingHeight;
            state.RotationDegrees ??= state.PendingRotationDegrees;
        }
        else if (state.Handler == "soun")
        {
            state.IsAudioTrack = true;
            state.AudioStreamCount++;
        }
    }

    private static async Task ReadSampleDescriptionAsync(
        VideoSourceReader source,
        long payloadStart,
        long payloadEnd,
        WalkState state,
        CancellationToken cancellationToken)
    {
        var counts = await source.ReadBytesAsync(payloadStart, 8, cancellationToken).ConfigureAwait(false);
        if (counts.Length < 8 || !VideoBinary.TryU32BE(counts, 4, out var entryCount) || entryCount == 0)
        {
            return;
        }

        var entryStart = payloadStart + 8;

        // A minimal audio sample entry is 36 bytes and a visual one is 86, so read what is actually there
        // rather than demanding the longest form from every track.
        var available = (int)Math.Min(40, payloadEnd - entryStart);
        if (available < 36)
        {
            return;
        }

        var entry = await source.ReadBytesAsync(entryStart, available, cancellationToken).ConfigureAwait(false);
        if (entry.Length < 36)
        {
            return;
        }

        var fourCc = Encoding.ASCII.GetString(entry, 4, 4);

        if (state.IsVideoTrack && state.Codec is null)
        {
            state.Codec = ResolveVideoCodec(fourCc);

            // Visual sample entry: width and height are 16-bit fields after 24 bytes of fixed header.
            if (VideoBinary.TryU16BE(entry, 32, out var width) &&
                VideoBinary.TryU16BE(entry, 34, out var height) &&
                width > 0 && height > 0)
            {
                state.Width ??= width;
                state.Height ??= height;
            }
        }
        else if (state.IsAudioTrack && state.AudioCodec is null)
        {
            state.AudioCodec = ResolveAudioCodec(fourCc);
        }
    }

    /// <summary>
    /// Reads the first time-to-sample entry, which gives the frame delta in the track's timescale and
    /// therefore the frame rate for constant-rate video.
    /// </summary>
    private static async Task ReadTimeToSampleAsync(
        VideoSourceReader source,
        long payloadStart,
        WalkState state,
        CancellationToken cancellationToken)
    {
        if (!state.IsVideoTrack || state.FrameRate is not null || state.TrackTimescale == 0)
        {
            return;
        }

        // stts: version and flags (4), entry count (4), then sample count and sample delta pairs.
        var payload = await source.ReadBytesAsync(payloadStart, 16, cancellationToken).ConfigureAwait(false);
        if (payload.Length < 16 || !VideoBinary.TryU32BE(payload, 12, out var sampleDelta) || sampleDelta == 0)
        {
            return;
        }

        state.FrameRate = Math.Round(state.TrackTimescale / (double)sampleDelta, 3);
    }

    private static async Task ReadItemListAsync(
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

            if (++state.AtomsVisited > MaxAtoms)
            {
                return;
            }

            var header = await source.ReadBytesAsync(position, 8, cancellationToken).ConfigureAwait(false);
            if (header.Length < 8 || !VideoBinary.TryU32BE(header, 0, out var size32) || size32 < 8)
            {
                return;
            }

            var isTitle = header[4] == 0xA9 &&
                          header[5] == (byte)'n' && header[6] == (byte)'a' && header[7] == (byte)'m';

            if (isTitle && state.Title is null)
            {
                // The item holds a 'data' child: 8 header bytes, 4 version and flags, 4 reserved, then text.
                var value = await source.ReadBytesAsync(position + 8 + 16, (int)Math.Min(256, size32 - 24), cancellationToken)
                    .ConfigureAwait(false);
                var text = Encoding.UTF8.GetString(value).TrimEnd('\0').Trim();
                if (text.Length > 0)
                {
                    state.Title = text;
                }
            }

            position += size32;
        }
    }

    internal static string ResolveVideoCodec(string fourCc) => fourCc switch
    {
        "avc1" or "avc3" => "h264",
        "hvc1" or "hev1" => "hevc",
        "vp08" => "vp8",
        "vp09" => "vp9",
        "av01" => "av1",
        "mp4v" => "mpeg4",
        "mpg4" => "mpeg4",
        "s263" or "h263" => "h263",
        "jpeg" => "mjpeg",
        "png " => "png",
        _ => fourCc.Trim().ToLowerInvariant()
    };

    internal static string ResolveAudioCodec(string fourCc) => fourCc switch
    {
        "mp4a" => "aac",
        "alac" => "alac",
        "ac-3" => "ac3",
        "ec-3" => "eac3",
        "Opus" or "opus" => "opus",
        "dtsc" or "dtsh" or "dtsl" => "dts",
        "samr" => "amr-nb",
        "sawb" => "amr-wb",
        "sowt" or "twos" => "pcm",
        "lpcm" => "pcm",
        ".mp3" or "69 6D" => "mp3",
        _ => fourCc.Trim().ToLowerInvariant()
    };

    private sealed class WalkState
    {
        public WalkState(VideoPluginOptions options)
        {
            Options = options;
        }

        public VideoPluginOptions Options { get; }
        public int AtomsVisited { get; set; }
        public bool SawMovieHeader { get; set; }
        public bool IsQuickTime { get; set; }
        public bool IsVideoTrack { get; set; }
        public bool IsAudioTrack { get; set; }
        public string? Handler { get; set; }
        public double? MovieDurationSeconds { get; set; }
        public double? TrackDurationSeconds { get; set; }
        public uint TrackTimescale { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public int? RotationDegrees { get; set; }
        public int? PendingWidth { get; set; }
        public int? PendingHeight { get; set; }
        public int? PendingRotationDegrees { get; set; }
        public int VideoStreamCount { get; set; }
        public int AudioStreamCount { get; set; }
        public string? Codec { get; set; }
        public string? AudioCodec { get; set; }
        public double? FrameRate { get; set; }
        public string? Title { get; set; }

        public void BeginTrack()
        {
            IsVideoTrack = false;
            IsAudioTrack = false;
            Handler = null;
            TrackDurationSeconds = null;
            TrackTimescale = 0;
            PendingWidth = null;
            PendingHeight = null;
            PendingRotationDegrees = null;
        }
    }
}
