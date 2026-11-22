using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Audio.Exceptions;
using SkyCD.Plugin.Metadata.Audio.Models;

namespace SkyCD.Plugin.Metadata.Audio.Parsing;

/// <summary>
/// Reads an MP3 source: ID3v2 tags and pictures, the MPEG audio frame header for stream facts, a Xing, Info
/// or VBRI header for exact variable-bitrate duration, and an ID3v1 tail as a fallback.
/// </summary>
internal static class MpegReader
{
    private const int Id3v1Length = 128;

    // Bitrate tables indexed by the four-bit frame index; index 0 is "free" and 15 is "bad".
    private static readonly int[] Mpeg1Layer1Bitrates =
        [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0];

    private static readonly int[] Mpeg1Layer23Bitrates =
        [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];

    private static readonly int[] Mpeg2Layer1Bitrates =
        [0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, 0];

    private static readonly int[] Mpeg2Layer23Bitrates =
        [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];

    private static readonly int[] Mpeg1SampleRates = [44100, 48000, 32000, 0];
    private static readonly int[] Mpeg2SampleRates = [22050, 24000, 16000, 0];
    private static readonly int[] Mpeg25SampleRates = [11025, 12000, 8000, 0];

    public static async Task<AudioProbe> ReadAsync(
        AudioSourceReader source,
        AudioPluginOptions options,
        CancellationToken cancellationToken)
    {
        var probe = new AudioProbe { Container = "mp3" };

        var id3v2Length = await ReadId3v2Async(source, probe, options, cancellationToken).ConfigureAwait(false);
        await ReadId3v1Async(source, probe, cancellationToken).ConfigureAwait(false);
        await ReadFrameHeaderAsync(source, probe, id3v2Length, options, cancellationToken).ConfigureAwait(false);

        probe.Tags.SetSource(ResolveTagSource(probe));
        return probe;
    }

    private static string ResolveTagSource(AudioProbe probe) =>
        probe.Tags.IsEmpty ? "none" : probe.Tags.Source ?? "id3v2";

    /// <summary>Returns the total length of the ID3v2 tag including its header, or 0 when absent.</summary>
    private static async Task<long> ReadId3v2Async(
        AudioSourceReader source,
        AudioProbe probe,
        AudioPluginOptions options,
        CancellationToken cancellationToken)
    {
        var header = await source.ReadBytesAsync(0, 10, cancellationToken).ConfigureAwait(false);
        if (header.Length < 10 || !AudioBinary.StartsWith(header, 0, "ID3"u8))
        {
            return 0;
        }

        var major = header[3];
        if (major is < 2 or > 4 || !AudioBinary.TrySynchsafe(header, 6, out var tagSize) || tagSize <= 0)
        {
            return 0;
        }

        var bodySize = Math.Min(tagSize, options.TagScanBytes);
        var body = await source.ReadBytesAsync(10, bodySize, cancellationToken).ConfigureAwait(false);

        // An extended header carries its own size and no tags this plugin needs.
        var offset = (header[5] & 0x40) != 0 ? SkipExtendedHeader(body, major) : 0;
        if (offset < 0)
        {
            return tagSize + 10;
        }

        var idLength = major == 2 ? 3 : 4;
        while (offset + idLength + (major == 2 ? 3 : 6) <= body.Length)
        {
            var frameId = AudioBinary.Ascii(body, offset, idLength);
            if (frameId.Length != idLength || frameId[0] == '\0')
            {
                break;
            }

            int frameSize;
            int payloadOffset;
            if (major == 2)
            {
                frameSize = (body[offset + 3] << 16) | (body[offset + 4] << 8) | body[offset + 5];
                payloadOffset = offset + 6;
            }
            else
            {
                // v2.4 stores frame sizes synchsafe; v2.3 stores them as a plain integer.
                var size = major == 4 && AudioBinary.TrySynchsafe(body, offset + idLength, out var synchsafe)
                    ? synchsafe
                    : ReadInt32BigEndian(body, offset + idLength);
                frameSize = size;
                payloadOffset = offset + idLength + 6;
            }

            if (frameSize <= 0 || payloadOffset + frameSize > body.Length)
            {
                break;
            }

            ApplyFrame(frameId, body.AsSpan(payloadOffset, frameSize), probe);
            offset = payloadOffset + frameSize;
        }

        if (!probe.Tags.IsEmpty)
        {
            probe.Tags.SetSource($"id3v2.{major}");
        }

        return tagSize + 10;
    }

    private static int SkipExtendedHeader(byte[] body, byte major)
    {
        if (major == 4)
        {
            // v2.4 extended header size includes itself.
            return AudioBinary.TrySynchsafe(body, 0, out var size) && size > 0 ? size : -1;
        }

        if (AudioBinary.TryU32BE(body, 0, out var legacySize) && legacySize >= 4)
        {
            return (int)legacySize + 4;
        }

        return -1;
    }

    private static int ReadInt32BigEndian(byte[] data, int offset)
    {
        if (!AudioBinary.TryU32BE(data, offset, out var value))
        {
            return 0;
        }

        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private static void ApplyFrame(string frameId, ReadOnlySpan<byte> payload, AudioProbe probe)
    {
        switch (frameId)
        {
            case "TIT2" or "TT2":
                probe.Tags.AddTitle(ReadTextFrame(payload));
                break;
            case "TPE1" or "TP1":
                probe.Tags.AddArtist(ReadTextFrame(payload));
                break;
            case "TALB" or "TAL":
                probe.Tags.AddAlbum(ReadTextFrame(payload));
                break;
            case "TPE2" or "TP2":
                probe.Tags.AddAlbumArtist(ReadTextFrame(payload));
                break;
            case "TCON" or "TCO":
                probe.Tags.AddGenre(ResolveGenre(ReadTextFrame(payload)));
                break;
            case "TRCK" or "TRK":
                var (track, trackTotal) = AudioBinary.ParsePair(ReadTextFrame(payload));
                probe.Tags.SetTrack(track, trackTotal);
                break;
            case "TPOS" or "TPA":
                var (disc, discTotal) = AudioBinary.ParsePair(ReadTextFrame(payload));
                probe.Tags.SetDisc(disc, discTotal);
                break;
            case "TYER" or "TYE":
            case "TDRC" or "TDRL":
                probe.Tags.SetYear(AudioBinary.ParseYear(ReadTextFrame(payload)));
                break;
            case "APIC" or "PIC":
                var picture = ReadPicture(frameId, payload);
                if (picture is not null)
                {
                    probe.Pictures.Add(picture);
                }

                break;
        }
    }

    /// <summary>
    /// Decodes a text frame. ID3v2.4 allows several values in one frame separated by NUL, and UTF-16 values
    /// are terminated by a two-byte NUL, so the payload is split on the encoding's terminator.
    /// </summary>
    private static string ReadTextFrame(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 2)
        {
            return string.Empty;
        }

        var encoding = payload[0];
        var body = payload[1..];
        var terminatorWidth = encoding is 1 or 2 ? 2 : 1;
        var values = new List<string>();
        var start = 0;

        while (start <= body.Length - terminatorWidth)
        {
            var end = FindTerminator(body, start, terminatorWidth);
            if (end < 0)
            {
                break;
            }

            var text = AudioBinary.DecodeId3Text(body[start..end], encoding);
            if (text.Length > 0)
            {
                values.Add(text);
            }

            start = end + terminatorWidth;
        }

        if (start == 0)
        {
            return AudioBinary.DecodeId3Text(body, encoding);
        }

        var tail = start < body.Length ? AudioBinary.DecodeId3Text(body[start..], encoding) : string.Empty;
        if (tail.Length > 0)
        {
            values.Add(tail);
        }

        return string.Join(AudioTags.MultiValueSeparator, values);
    }

    private static int FindTerminator(ReadOnlySpan<byte> body, int start, int terminatorWidth)
    {
        for (var i = start; i + terminatorWidth <= body.Length; i++)
        {
            if (body[i] != 0)
            {
                continue;
            }

            if (terminatorWidth == 1 || body[i + 1] == 0)
            {
                return i;
            }

            i++;
        }

        return -1;
    }

    private static AudioPicture? ReadPicture(string frameId, ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 4)
        {
            return null;
        }

        var encoding = payload[0];
        var position = 1;
        string contentType;

        if (frameId == "PIC")
        {
            // v2.2 stores a three-character image format instead of a MIME type.
            if (position + 3 > payload.Length)
            {
                return null;
            }

            contentType = AudioBinary.Ascii(payload, position, 3).ToUpperInvariant() switch
            {
                "PNG" => "image/png",
                "JPG" or "JPEG" => "image/jpeg",
                _ => "application/octet-stream"
            };
            position += 3;
        }
        else
        {
            var end = payload[position..].IndexOf((byte)0);
            if (end < 0)
            {
                return null;
            }

            contentType = AudioBinary.Ascii(payload, position, end);
            position += end + 1;
        }

        if (position >= payload.Length)
        {
            return null;
        }

        var pictureType = payload[position++];
        position = SkipDescription(payload, position, encoding);
        if (position < 0 || position >= payload.Length)
        {
            return null;
        }

        return new AudioPicture(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            payload[position..].ToArray())
        {
            IsFrontCover = pictureType == 3
        };
    }

    private static int SkipDescription(ReadOnlySpan<byte> payload, int position, byte encoding)
    {
        var terminatorWidth = encoding is 1 or 2 ? 2 : 1;
        for (var i = position; i + terminatorWidth <= payload.Length; i++)
        {
            if (payload[i] != 0)
            {
                continue;
            }

            if (terminatorWidth == 1)
            {
                return i + 1;
            }

            if (payload[i + 1] == 0)
            {
                return i + 2;
            }

            i++;
        }

        return -1;
    }

    private static async Task ReadId3v1Async(
        AudioSourceReader source,
        AudioProbe probe,
        CancellationToken cancellationToken)
    {
        if (source.Length < Id3v1Length)
        {
            return;
        }

        var tail = await source.ReadTailAsync(Id3v1Length, cancellationToken).ConfigureAwait(false);
        if (tail.Length < Id3v1Length || !AudioBinary.StartsWith(tail, 0, "TAG"u8))
        {
            return;
        }

        // ID3v1 is a fixed-width Latin-1 block; ID3v2 values already collected win over these.
        var title = AudioBinary.Ascii(tail, 3, 30);
        var artist = AudioBinary.Ascii(tail, 33, 30);
        var album = AudioBinary.Ascii(tail, 63, 30);
        var year = AudioBinary.Ascii(tail, 93, 4);
        var genreIndex = tail[127];

        if (probe.Tags.IsEmpty)
        {
            probe.Tags.SetSource("id3v1");
        }

        probe.Tags.AddTitle(title);
        probe.Tags.AddArtist(artist);
        probe.Tags.AddAlbum(album);
        probe.Tags.SetYear(AudioBinary.ParseYear(year));

        // ID3v1.1 steals the last two comment bytes for a track number when the first is zero.
        if (tail[125] == 0 && tail[126] != 0)
        {
            probe.Tags.SetTrack(tail[126], null);
        }

        if (genreIndex < Id3Genres.All.Length)
        {
            probe.Tags.AddGenre(Id3Genres.All[genreIndex]);
        }
    }

    private static async Task ReadFrameHeaderAsync(
        AudioSourceReader source,
        AudioProbe probe,
        long audioStart,
        AudioPluginOptions options,
        CancellationToken cancellationToken)
    {
        probe.Codec = "mp3";

        var scanLimit = (int)Math.Min(options.HeaderScanBytes, Math.Max(0, source.Length - audioStart));
        if (scanLimit < 4)
        {
            throw new AudioDecodeException("the source ends before any MPEG audio frame");
        }

        var window = await source.ReadBytesAsync(audioStart, scanLimit, cancellationToken).ConfigureAwait(false);
        var frameOffset = FindFrameHeader(window);
        if (frameOffset < 0)
        {
            // Without a frame header there is no evidence this is audio at all, so a file that merely carries
            // an .mp3 extension must fail rather than report an empty result.
            throw new AudioDecodeException("no MPEG audio frame header was found");
        }

        if (!TryParseFrameHeader(window, frameOffset, out var frame))
        {
            return;
        }

        probe.SampleRateHz = frame.SampleRate;
        probe.Channels = frame.Channels;
        probe.BitrateKbps = frame.BitrateKbps;

        var audioBytes = source.Length - audioStart - frameOffset;
        if (TryReadVbrHeader(window, frameOffset, frame, out var vbrFrames, out var vbrBytes))
        {
            probe.DurationSeconds = (double)vbrFrames * frame.SamplesPerFrame / frame.SampleRate;
            if (vbrBytes > 0 && probe.DurationSeconds > 0)
            {
                probe.BitrateKbps = (int)Math.Round(vbrBytes * 8.0 / probe.DurationSeconds.Value / 1000.0);
            }
        }
        else if (frame.BitrateKbps > 0)
        {
            // Constant bitrate estimate from the bytes that follow the tag.
            probe.DurationSeconds = Math.Max(0, audioBytes - frame.FrameSize) * 8.0 / (frame.BitrateKbps * 1000);
        }
    }

    private static int FindFrameHeader(byte[] window)
    {
        for (var i = 0; i + 4 <= window.Length; i++)
        {
            if (window[i] != 0xFF || (window[i + 1] & 0xE0) != 0xE0)
            {
                continue;
            }

            // A single header can be coincidence; require the next frame to land where this one says.
            if (!TryParseFrameHeader(window, i, out var frame) || frame.FrameSize <= 0)
            {
                continue;
            }

            if (i + frame.FrameSize + 2 > window.Length)
            {
                return i;
            }

            if (window[i + frame.FrameSize] == 0xFF && (window[i + frame.FrameSize + 1] & 0xE0) == 0xE0)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryParseFrameHeader(byte[] data, int offset, out MpegFrame frame)
    {
        frame = default;
        if (offset + 4 > data.Length)
        {
            return false;
        }

        var b1 = data[offset + 1];
        var b2 = data[offset + 2];
        var b3 = data[offset + 3];

        var versionBits = (b1 >> 3) & 0x03;
        var layerBits = (b1 >> 1) & 0x03;
        if (versionBits == 1 || layerBits == 0)
        {
            return false;
        }

        var bitrateIndex = (b2 >> 4) & 0x0F;
        var sampleRateIndex = (b2 >> 2) & 0x03;
        var padding = (b2 >> 1) & 0x01;
        var channelMode = (b3 >> 6) & 0x03;
        if (bitrateIndex is 0 or 15 || sampleRateIndex == 3)
        {
            return false;
        }

        var mpeg1 = versionBits == 3;
        var mpeg25 = versionBits == 0;
        var layer = layerBits switch { 1 => 3, 2 => 2, 3 => 1, _ => 0 };

        var sampleRate = (mpeg1 ? Mpeg1SampleRates : mpeg25 ? Mpeg25SampleRates : Mpeg2SampleRates)[sampleRateIndex];
        if (sampleRate == 0)
        {
            return false;
        }

        var bitrate = (mpeg1
            ? (layer == 1 ? Mpeg1Layer1Bitrates : Mpeg1Layer23Bitrates)
            : (layer == 1 ? Mpeg2Layer1Bitrates : Mpeg2Layer23Bitrates))[bitrateIndex];
        if (bitrate == 0)
        {
            return false;
        }

        var samplesPerFrame = layer switch
        {
            1 => 384,
            2 => 1152,
            _ => mpeg1 ? 1152 : 576
        };

        var frameSize = layer == 1
            ? (12 * bitrate * 1000 / sampleRate + padding) * 4
            : samplesPerFrame / 8 * bitrate * 1000 / sampleRate + padding;

        frame = new MpegFrame(
            sampleRate,
            bitrate,
            channelMode == 3 ? 1 : 2,
            samplesPerFrame,
            frameSize,
            mpeg1,
            mpeg25,
            layer,
            channelMode);

        return true;
    }

    /// <summary>Reads a Xing, Info or VBRI header, which is what makes VBR duration exact.</summary>
    private static bool TryReadVbrHeader(byte[] data, int frameOffset, in MpegFrame frame, out uint frames, out uint bytes)
    {
        frames = 0;
        bytes = 0;

        // Side-information size differs by MPEG version and channel mode, and the Xing tag follows it.
        var sideInfoSize = frame.IsMpeg1 ? (frame.ChannelMode == 3 ? 17 : 32) : (frame.ChannelMode == 3 ? 9 : 17);
        var xingOffset = frameOffset + 4 + sideInfoSize;

        if (AudioBinary.StartsWith(data, xingOffset, "Xing"u8) || AudioBinary.StartsWith(data, xingOffset, "Info"u8))
        {
            if (!AudioBinary.TryU32BE(data, xingOffset + 4, out var flags))
            {
                return false;
            }

            var position = xingOffset + 8;
            if ((flags & 0x01) != 0)
            {
                if (!AudioBinary.TryU32BE(data, position, out frames))
                {
                    return false;
                }

                position += 4;
            }

            if ((flags & 0x02) != 0)
            {
                AudioBinary.TryU32BE(data, position, out bytes);
            }

            return frames > 0;
        }

        // VBRI is always 32 bytes after the frame header's side information for MPEG1 stereo.
        var vbriOffset = frameOffset + 4 + 32;
        if (AudioBinary.StartsWith(data, vbriOffset, "VBRI"u8))
        {
            if (!AudioBinary.TryU32BE(data, vbriOffset + 10, out bytes) ||
                !AudioBinary.TryU32BE(data, vbriOffset + 14, out frames))
            {
                return false;
            }

            return frames > 0;
        }

        return false;
    }

    /// <summary>
    /// Resolves the ID3v1 numeric genre form, which writers store as <c>(17)</c> or as a refinement such as
    /// <c>(CR)Booty Bass</c>. Anything not in parentheses is already a name and passes through.
    /// </summary>
    internal static string? ResolveGenre(string? genre)
    {
        if (string.IsNullOrWhiteSpace(genre))
        {
            return genre;
        }

        var text = genre.Trim();
        if (text[0] != '(')
        {
            return text;
        }

        var close = text.IndexOf(')');
        if (close < 0)
        {
            return text;
        }

        var code = text[1..close];
        var refinement = text[(close + 1)..].Trim();

        if (int.TryParse(code, out var index) && index >= 0 && index < Id3Genres.All.Length)
        {
            return string.IsNullOrEmpty(refinement) ? Id3Genres.All[index] : refinement;
        }

        return string.IsNullOrEmpty(refinement) ? text : refinement;
    }

    private readonly record struct MpegFrame(
        int SampleRate,
        int BitrateKbps,
        int Channels,
        int SamplesPerFrame,
        int FrameSize,
        bool IsMpeg1,
        bool IsMpeg25,
        int Layer,
        int ChannelMode);
}
