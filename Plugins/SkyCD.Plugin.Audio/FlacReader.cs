using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Audio.Exceptions;

namespace SkyCD.Plugin.Audio;

/// <summary>
/// Reads a FLAC source: the STREAMINFO block for stream facts and exact duration, VORBIS_COMMENT for tags,
/// and PICTURE for embedded cover art.
/// </summary>
internal static class FlacReader
{
    private const int MaxBlocks = 64;

    public static async Task<AudioProbe> ReadAsync(
        AudioSourceReader source,
        AudioPluginOptions options,
        CancellationToken cancellationToken)
    {
        var header = await source.ReadBytesAsync(0, 4, cancellationToken).ConfigureAwait(false);
        if (!AudioBinary.StartsWith(header, 0, "fLaC"u8))
        {
            throw new AudioDecodeException("the source does not start with a FLAC marker");
        }

        var probe = new AudioProbe { Container = "flac", Codec = "flac" };
        var position = 4L;
        var sawStreamInfo = false;

        for (var block = 0; block < MaxBlocks; block++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var blockHeader = await source.ReadBytesAsync(position, 4, cancellationToken).ConfigureAwait(false);
            if (blockHeader.Length < 4)
            {
                break;
            }

            var isLast = (blockHeader[0] & 0x80) != 0;
            var blockType = blockHeader[0] & 0x7F;
            if (!AudioBinary.TryU24BE(blockHeader, 1, out var blockLength) || blockLength > options.TagScanBytes)
            {
                break;
            }

            position += 4;

            switch (blockType)
            {
                case 0:
                    await ReadStreamInfoAsync(source, probe, position, cancellationToken).ConfigureAwait(false);
                    sawStreamInfo = true;
                    break;
                case 4:
                    await ReadVorbisCommentAsync(source, probe, position, (int)blockLength, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case 6:
                    await ReadPictureAsync(source, probe, position, (int)blockLength, cancellationToken)
                        .ConfigureAwait(false);
                    break;
            }

            position += blockLength;
            if (isLast || position >= source.Length)
            {
                break;
            }
        }

        if (!sawStreamInfo || probe.SampleRateHz is null)
        {
            throw new AudioDecodeException("the FLAC stream has no readable STREAMINFO block");
        }

        if (!probe.Tags.IsEmpty)
        {
            probe.Tags.SetSource("vorbis-comment");
        }
        else
        {
            probe.Tags.SetSource("none");
        }

        // FLAC stores no bitrate; the average over the audio payload is the honest figure.
        if (probe.DurationSeconds is > 0 && position < source.Length)
        {
            probe.BitrateKbps = (int)Math.Round((source.Length - position) * 8.0 / probe.DurationSeconds.Value / 1000.0);
        }

        return probe;
    }

    private static async Task ReadStreamInfoAsync(
        AudioSourceReader source,
        AudioProbe probe,
        long offset,
        CancellationToken cancellationToken)
    {
        // Bytes 10..17 of STREAMINFO pack sample rate (20), channels-1 (3), bits per sample-1 (5) and
        // total samples (36) into one 64-bit field.
        var packed = await source.ReadBytesAsync(offset + 10, 8, cancellationToken).ConfigureAwait(false);
        if (packed.Length < 8 || !AudioBinary.TryU64BE(packed, 0, out var combined))
        {
            return;
        }

        var sampleRate = (int)(combined >> 44);
        var channels = (int)((combined >> 41) & 0x07) + 1;
        var bitDepth = (int)((combined >> 36) & 0x1F) + 1;
        var totalSamples = combined & 0x0F_FFFF_FFFF;

        if (sampleRate > 0)
        {
            probe.SampleRateHz = sampleRate;
        }

        if (channels is > 0 and <= 8)
        {
            probe.Channels = channels;
        }

        if (bitDepth is > 0 and <= 32)
        {
            probe.BitDepth = bitDepth;
        }

        if (sampleRate > 0 && totalSamples > 0)
        {
            probe.DurationSeconds = (double)totalSamples / sampleRate;
        }
    }

    /// <summary>
    /// Reads a Vorbis comment block, which FLAC and Ogg share: a vendor string, a count, then length-prefixed
    /// <c>KEY=value</c> pairs in UTF-8. Repeated keys are legitimate and are merged.
    /// </summary>
    internal static void ApplyVorbisComments(AudioProbe probe, ReadOnlySpan<byte> block)
    {
        var position = 0;
        if (!AudioBinary.TryU32LE(block, position, out var vendorLength) || vendorLength > block.Length)
        {
            return;
        }

        position += 4 + (int)vendorLength;
        if (!AudioBinary.TryU32LE(block, position, out var commentCount) || commentCount > 1024)
        {
            return;
        }

        position += 4;

        for (var index = 0; index < commentCount; index++)
        {
            if (!AudioBinary.TryU32LE(block, position, out var length) || length == 0)
            {
                return;
            }

            position += 4;
            if (position + length > block.Length)
            {
                return;
            }

            var text = Encoding.UTF8.GetString(block.Slice(position, (int)length));
            position += (int)length;

            var separator = text.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            ApplyComment(probe, text[..separator].Trim(), text[(separator + 1)..].Trim());
        }
    }

    private static void ApplyComment(AudioProbe probe, string key, string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        switch (key.ToUpperInvariant())
        {
            case "TITLE":
                probe.Tags.AddTitle(value);
                break;
            case "ARTIST":
                probe.Tags.AddArtist(value);
                break;
            case "ALBUM":
                probe.Tags.AddAlbum(value);
                break;
            case "ALBUMARTIST":
                probe.Tags.AddAlbumArtist(value);
                break;
            case "GENRE":
                probe.Tags.AddGenre(MpegReader.ResolveGenre(value));
                break;
            case "TRACKNUMBER":
                probe.Tags.SetTrack(AudioBinary.ParseInt(value), probe.Tags.TrackCount);
                break;
            case "TRACKTOTAL":
            case "TOTALTRACKS":
                probe.Tags.SetTrack(probe.Tags.TrackNumber, AudioBinary.ParseInt(value));
                break;
            case "DISCNUMBER":
                probe.Tags.SetDisc(AudioBinary.ParseInt(value), probe.Tags.DiscCount);
                break;
            case "DISCTOTAL":
            case "TOTALDISCS":
                probe.Tags.SetDisc(probe.Tags.DiscNumber, AudioBinary.ParseInt(value));
                break;
            case "DATE":
            case "YEAR":
            case "ORIGINALDATE":
                probe.Tags.SetYear(AudioBinary.ParseYear(value));
                break;
        }
    }

    private static async Task ReadVorbisCommentAsync(
        AudioSourceReader source,
        AudioProbe probe,
        long offset,
        int length,
        CancellationToken cancellationToken)
    {
        var block = await source.ReadBytesAsync(offset, length, cancellationToken).ConfigureAwait(false);
        ApplyVorbisComments(probe, block);
    }

    private static async Task ReadPictureAsync(
        AudioSourceReader source,
        AudioProbe probe,
        long offset,
        int length,
        CancellationToken cancellationToken)
    {
        var block = await source.ReadBytesAsync(offset, length, cancellationToken).ConfigureAwait(false);
        var picture = ParsePictureBlock(block);
        if (picture is not null)
        {
            probe.Pictures.Add(picture);
        }
    }

    /// <summary>
    /// Parses the FLAC PICTURE block layout, which is also what Ogg embeds base64-encoded under the
    /// <c>METADATA_BLOCK_PICTURE</c> comment.
    /// </summary>
    internal static AudioPicture? ParsePictureBlock(ReadOnlySpan<byte> block)
    {
        var position = 0;
        if (!AudioBinary.TryU32BE(block, position, out var pictureType))
        {
            return null;
        }

        position += 4;

        if (!AudioBinary.TryU32BE(block, position, out var mimeLength) ||
            position + 4 + mimeLength > block.Length)
        {
            return null;
        }

        position += 4;
        var contentType = AudioBinary.Ascii(block, position, (int)mimeLength);
        position += (int)mimeLength;

        if (!AudioBinary.TryU32BE(block, position, out var descriptionLength) ||
            position + 4 + descriptionLength > block.Length)
        {
            return null;
        }

        position += 4 + (int)descriptionLength;

        // Width, height, colour depth and palette entries are descriptive and not needed for a preview.
        position += 16;

        if (!AudioBinary.TryU32BE(block, position, out var dataLength) ||
            dataLength == 0 || position + 4 + dataLength > block.Length)
        {
            return null;
        }

        position += 4;
        return new AudioPicture(
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            block.Slice(position, (int)dataLength).ToArray())
        {
            IsFrontCover = pictureType == 3
        };
    }
}
