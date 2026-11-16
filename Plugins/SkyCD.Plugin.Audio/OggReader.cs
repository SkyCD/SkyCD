using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Audio.Exceptions;

namespace SkyCD.Plugin.Audio;

/// <summary>
/// Reads an Ogg Vorbis source: the identification header for stream facts, the comment header for tags and
/// base64 cover art, and the final page's granule position for exact duration.
/// </summary>
internal static class OggReader
{
    private const int PageHeaderLength = 27;
    private const int TailScanBytes = 64 * 1024;
    private const int MaxHeaderPages = 32;

    public static async Task<AudioProbe> ReadAsync(
        AudioSourceReader source,
        AudioPluginOptions options,
        CancellationToken cancellationToken)
    {
        var probe = new AudioProbe { Container = "ogg", Codec = "vorbis" };

        var (identification, afterIdentification) =
            await ReadPacketAsync(source, 0, options, cancellationToken).ConfigureAwait(false);
        if (identification.Length < 30 || identification[0] != 0x01 || !AudioBinary.StartsWith(identification, 1, "vorbis"u8))
        {
            throw new AudioDecodeException("the Ogg stream does not start with a Vorbis identification header");
        }

        ApplyIdentification(probe, identification);

        var (comments, _) =
            await ReadPacketAsync(source, afterIdentification, options, cancellationToken).ConfigureAwait(false);
        if (comments.Length > 7 && comments[0] == 0x03 && AudioBinary.StartsWith(comments, 1, "vorbis"u8))
        {
            FlacReader.ApplyVorbisComments(probe, comments.AsSpan(7));
            ApplyEmbeddedPictures(probe, comments.AsSpan(7));
        }

        var lastGranule = await ReadLastGranulePositionAsync(source, cancellationToken).ConfigureAwait(false);
        if (lastGranule > 0 && probe.SampleRateHz is > 0)
        {
            probe.DurationSeconds = (double)lastGranule / probe.SampleRateHz.Value;

            // The identification header's nominal bitrate is what the writer declared; only fall back to a
            // whole-file average when it was left unset.
            if (probe.BitrateKbps is null && probe.DurationSeconds > 0)
            {
                probe.BitrateKbps = (int)Math.Round(source.Length * 8.0 / probe.DurationSeconds.Value / 1000.0);
            }
        }

        probe.Tags.SetSource(probe.Tags.IsEmpty ? "none" : "vorbis-comment");
        return probe;
    }

    private static void ApplyIdentification(AudioProbe probe, ReadOnlySpan<byte> packet)
    {
        // Identification header after the 7-byte type marker: version, channels, sample rate, three bitrates,
        // block sizes and a framing bit.
        if (!AudioBinary.TryU32LE(packet, 12, out var sampleRate) || sampleRate == 0)
        {
            return;
        }

        probe.Channels = packet[11];
        probe.SampleRateHz = (int)sampleRate;

        // The nominal bitrate is what writers mean by "the bitrate"; it may be zero when unset.
        // Layout after the 7-byte type marker and 4-byte version: channels, sample rate, then the maximum,
        // nominal and minimum bitrates.
        if (AudioBinary.TryU32LE(packet, 20, out var nominal) && nominal > 0)
        {
            probe.BitrateKbps = (int)(nominal / 1000);
        }
    }

    /// <summary>
    /// Reassembles one logical packet, which an Ogg stream may split across pages and which may share a page
    /// with the packet before it. Returns the packet and the offset its page ended at.
    /// </summary>
    private static async Task<(byte[] Packet, long NextPosition)> ReadPacketAsync(
        AudioSourceReader source,
        long start,
        AudioPluginOptions options,
        CancellationToken cancellationToken)
    {
        using var packet = new System.IO.MemoryStream();
        var position = start;
        var started = false;

        for (var page = 0; page < MaxHeaderPages && position < source.Length; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var header = await source.ReadBytesAsync(position, PageHeaderLength, cancellationToken).ConfigureAwait(false);
            if (header.Length < PageHeaderLength || !AudioBinary.StartsWith(header, 0, "OggS"u8))
            {
                break;
            }

            var segmentCount = header[26];
            var table = await source.ReadBytesAsync(position + PageHeaderLength, segmentCount, cancellationToken)
                .ConfigureAwait(false);
            if (table.Length < segmentCount)
            {
                break;
            }

            var payloadLength = 0;
            for (var i = 0; i < segmentCount; i++)
            {
                payloadLength += table[i];
            }

            var payloadOffset = position + PageHeaderLength + segmentCount;
            position = payloadOffset + payloadLength;

            if (payloadLength > options.TagScanBytes)
            {
                break;
            }

            var payload = await source.ReadBytesAsync(payloadOffset, payloadLength, cancellationToken).ConfigureAwait(false);

            var cursor = 0;
            var continues = false;
            for (var i = 0; i < segmentCount && cursor < payload.Length; i++)
            {
                var segment = table[i];
                var take = Math.Min(segment, payload.Length - cursor);
                packet.Write(payload, cursor, take);
                cursor += take;
                continues = segment == 255;
                if (!continues)
                {
                    started = true;
                    break;
                }
            }

            if (started && !continues)
            {
                break;
            }
        }

        return (packet.ToArray(), position);
    }

    /// <summary>
    /// Reads the granule position of the final page, which is the total number of decoded samples. Scanning
    /// backwards from the tail avoids reading a file that may be gigabytes long.
    /// </summary>
    private static async Task<long> ReadLastGranulePositionAsync(AudioSourceReader source, CancellationToken cancellationToken)
    {
        var size = (int)Math.Min(TailScanBytes, source.Length);
        var tail = await source.ReadBytesAsync(source.Length - size, size, cancellationToken).ConfigureAwait(false);

        for (var i = tail.Length - PageHeaderLength; i >= 0; i--)
        {
            if (!AudioBinary.StartsWith(tail, i, "OggS"u8))
            {
                continue;
            }

            // Bytes 6..13 hold the granule position; -1 marks a page that carries no completed samples.
            if (!AudioBinary.TryU64LE(tail, i + 6, out var granule) || granule == ulong.MaxValue)
            {
                continue;
            }

            return (long)granule;
        }

        return -1;
    }

    /// <summary>
    /// Extracts cover art from <c>METADATA_BLOCK_PICTURE</c> comments, which carry a base64-encoded FLAC
    /// PICTURE block, and from the legacy <c>COVERART</c> convention, which carries raw base64 image bytes.
    /// </summary>
    private static void ApplyEmbeddedPictures(AudioProbe probe, ReadOnlySpan<byte> comments)
    {
        var position = 0;
        if (!AudioBinary.TryU32LE(comments, position, out var vendorLength) || vendorLength > comments.Length)
        {
            return;
        }

        position += 4 + (int)vendorLength;
        if (!AudioBinary.TryU32LE(comments, position, out var commentCount) || commentCount > 1024)
        {
            return;
        }

        position += 4;

        for (var index = 0; index < commentCount; index++)
        {
            if (!AudioBinary.TryU32LE(comments, position, out var length) || length == 0)
            {
                return;
            }

            position += 4;
            if (position + length > comments.Length)
            {
                return;
            }

            var text = System.Text.Encoding.UTF8.GetString(comments.Slice(position, (int)length));
            position += (int)length;

            var separator = text.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = text[..separator].Trim().ToUpperInvariant();
            var value = text[(separator + 1)..].Trim();

            if (key == "METADATA_BLOCK_PICTURE")
            {
                AddDecoded(probe, value, fromPictureBlock: true);
            }
            else if (key == "COVERART")
            {
                AddDecoded(probe, value, fromPictureBlock: false);
            }
        }
    }

    private static void AddDecoded(AudioProbe probe, string base64, bool fromPictureBlock)
    {
        byte[] decoded;
        try
        {
            decoded = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return;
        }

        if (fromPictureBlock)
        {
            var picture = FlacReader.ParsePictureBlock(decoded);
            if (picture is not null)
            {
                probe.Pictures.Add(picture);
            }

            return;
        }

        probe.Pictures.Add(new AudioPicture(SniffImageContentType(decoded), decoded) { IsFrontCover = true });
    }

    /// <summary>Identifies an image by its magic bytes, since raw cover art carries no declared MIME type.</summary>
    internal static string SniffImageContentType(ReadOnlySpan<byte> data)
    {
        if (AudioBinary.StartsWith(data, 0, "\x89PNG\r\n\x1A\n"u8))
        {
            return "image/png";
        }

        if (data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length > 12 && AudioBinary.StartsWith(data, 0, "RIFF"u8) && AudioBinary.StartsWith(data, 8, "WEBP"u8))
        {
            return "image/webp";
        }

        return "application/octet-stream";
    }
}
