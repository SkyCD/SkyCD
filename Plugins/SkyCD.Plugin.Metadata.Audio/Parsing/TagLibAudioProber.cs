using System;
using System.IO;
using System.Linq;
using System.Threading;
using SkyCD.Plugin.Metadata.Audio.Exceptions;

namespace SkyCD.Plugin.Metadata.Audio.Parsing;

/// <summary>
/// Reads tags and stream facts through TagLibSharp, mapping them onto the catalog's normalized schema.
/// </summary>
/// <remarks>
/// The plugin owns the field names, units and empty-state rules; TagLibSharp only supplies the raw values.
/// Its own multi-value conventions differ from the catalog's, so every repeated field is merged through
/// <see cref="AudioTags.Merge"/> instead of being written straight through.
/// </remarks>
internal static class TagLibAudioProber
{
    private const string Mp3 = "mp3";
    private const string Flac = "flac";
    private const string Ogg = "ogg";
    private const string Mp4 = "mp4";

    public static AudioProbe Probe(AudioSourceReader reader, string container, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var abstraction = new AudioSourceFile(reader, "source" + ExtensionFor(container), cancellationToken);
        try
        {
            using var file = TagLib.File.Create(abstraction, MimeFor(container), TagLib.ReadStyle.Average);
            return Map(file, container);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            // The host's read-budget exception is an InvalidOperationException and must reach the host as-is.
            throw;
        }
        catch (TagLib.CorruptFileException exception)
        {
            throw new AudioDecodeException($"the {container} structure is corrupt: {exception.Message}", exception);
        }
        catch (TagLib.UnsupportedFormatException exception)
        {
            throw new AudioDecodeException($"the {container} structure is not supported: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is EndOfStreamException or ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            // TagLibSharp's readers throw plain BCL exceptions on malformed length fields; none of them mean the
            // source was readable, so they are converted to the plugin's own failure instead of leaking.
            throw new AudioDecodeException($"the {container} structure is malformed: {exception.Message}", exception);
        }
    }

    private static AudioProbe Map(TagLib.File file, string container)
    {
        var probe = new AudioProbe { Container = container };
        var tag = file.Tag;

        // TagLib keeps only the first of several text frames with the same identifier, while the catalog merges
        // them, so repeated ID3v2 frames are read here before the tag-level values fill in the rest.
        if (file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3v2)
        {
            MergeRepeatedId3v2Frames(id3v2, probe.Tags);
        }

        probe.Tags.AddTitle(tag.Title);
        foreach (var performer in tag.Performers)
        {
            probe.Tags.AddArtist(performer);
        }

        probe.Tags.AddAlbum(tag.Album);
        foreach (var albumArtist in tag.AlbumArtists)
        {
            probe.Tags.AddAlbumArtist(albumArtist);
        }

        if (tag.Genres.Length > 0)
        {
            foreach (var genre in tag.Genres)
            {
                probe.Tags.AddGenre(genre);
            }
        }
        else
        {
            probe.Tags.AddGenre(tag.FirstGenre);
        }

        probe.Tags.SetTrack(Positive(tag.Track), Positive(tag.TrackCount));
        probe.Tags.SetDisc(Positive(tag.Disc), Positive(tag.DiscCount));
        probe.Tags.SetYear(Positive(tag.Year));
        probe.Tags.SetSource(SourceFor(file));

        foreach (var picture in tag.Pictures)
        {
            var data = picture.Data.Data;
            if (data.Length == 0)
            {
                continue;
            }

            probe.Pictures.Add(new AudioPicture(ContentTypeOf(picture, data), data)
            {
                IsFrontCover = picture.Type == TagLib.PictureType.FrontCover
            });
        }

        var properties = file.Properties;
        if (properties.Duration > TimeSpan.Zero)
        {
            probe.DurationSeconds = properties.Duration.TotalSeconds;
        }

        var sampleEntry = container == Mp4 ? FindAudioSampleEntry(properties) : null;
        probe.BitrateKbps = Positive(properties.AudioBitrate);
        probe.SampleRateHz = Positive(properties.AudioSampleRate);
        probe.Channels = Positive(properties.AudioChannels);

        // MP4 audio properties do not surface the sample size, but the sample entry records it.
        probe.BitDepth = Positive(properties.BitsPerSample) ?? Positive(sampleEntry?.AudioSampleSize ?? 0);
        probe.Codec = CodecFor(container, properties, sampleEntry);
        return probe;
    }

    private static void MergeRepeatedId3v2Frames(TagLib.Id3v2.Tag id3v2, AudioTags tags)
    {
        AddFrameTexts(id3v2, "TIT2", tags.AddTitle);
        AddFrameTexts(id3v2, "TPE1", tags.AddArtist);
        AddFrameTexts(id3v2, "TALB", tags.AddAlbum);
        AddFrameTexts(id3v2, "TPE2", tags.AddAlbumArtist);
    }

    private static void AddFrameTexts(TagLib.Id3v2.Tag id3v2, string frameId, Action<string?> add)
    {
        var id = TagLib.ByteVector.FromString(frameId, TagLib.StringType.Latin1);
        foreach (var frame in id3v2.GetFrames(id).OfType<TagLib.Id3v2.TextInformationFrame>())
        {
            foreach (var text in frame.Text)
            {
                add(text);
            }
        }
    }

    private static TagLib.Mpeg4.IsoAudioSampleEntry? FindAudioSampleEntry(TagLib.Properties properties) =>
        properties.Codecs.OfType<TagLib.Mpeg4.IsoAudioSampleEntry>().FirstOrDefault();

    /// <summary>
    /// Names the scheme that produced the values, using what TagLib actually found on disk rather than the
    /// in-memory union, so a source with no tag block reports no source at all.
    /// </summary>
    private static string? SourceFor(TagLib.File file)
    {
        var onDisk = file.TagTypesOnDisk;

        if ((onDisk & TagLib.TagTypes.Id3v2) != 0)
        {
            return file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3v2
                ? $"id3v2.{id3v2.Version}"
                : "id3v2";
        }

        if ((onDisk & TagLib.TagTypes.Ape) != 0)
        {
            return "ape";
        }

        if ((onDisk & TagLib.TagTypes.Apple) != 0)
        {
            return "itunes-atom";
        }

        if ((onDisk & TagLib.TagTypes.Xiph) != 0)
        {
            return "vorbis-comment";
        }

        return (onDisk & TagLib.TagTypes.Id3v1) != 0 ? "id3v1" : null;
    }

    private static string? CodecFor(
        string container,
        TagLib.Properties properties,
        TagLib.Mpeg4.IsoAudioSampleEntry? sampleEntry)
    {
        switch (container)
        {
            case Mp3:
                return "mp3";
            case Flac:
                return "flac";
            case Mp4:
                // The four-character sample entry tells AAC apart from ALAC; anything else is reported raw.
                return sampleEntry?.BoxType.ToString() switch
                {
                    null => null,
                    "mp4a" => "aac",
                    "alac" => "alac",
                    var boxType => boxType
                };
            default:
                // Ogg can carry more than Vorbis; the codec object is the only reliable discriminator.
                foreach (var codec in properties.Codecs)
                {
                    var name = codec?.GetType().Name;
                    if (name is null)
                    {
                        continue;
                    }

                    if (name.Contains("Vorbis", StringComparison.OrdinalIgnoreCase))
                    {
                        return "vorbis";
                    }

                    if (name.Contains("Opus", StringComparison.OrdinalIgnoreCase))
                    {
                        return "opus";
                    }

                    if (name.Contains("Speex", StringComparison.OrdinalIgnoreCase))
                    {
                        return "speex";
                    }
                }

                return null;
        }
    }

    private static string ContentTypeOf(TagLib.IPicture picture, byte[] data)
    {
        if (!string.IsNullOrWhiteSpace(picture.MimeType))
        {
            return picture.MimeType.Trim();
        }

        // Older APIC frames may carry "JPG" or nothing at all; the first bytes identify the format reliably.
        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return "image/png";
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 12 && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' &&
            data[3] == (byte)'F' && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' &&
            data[11] == (byte)'P')
        {
            return "image/webp";
        }

        return "application/octet-stream";
    }

    private static int? Positive(int value) => value > 0 ? value : null;

    private static int? Positive(uint value) => value > 0 ? (int)value : null;

    private static string ExtensionFor(string container) => container switch
    {
        Mp3 => ".mp3",
        Flac => ".flac",
        Ogg => ".ogg",
        _ => ".m4a"
    };

    private static string MimeFor(string container) => container switch
    {
        Mp3 => "audio/mpeg",
        Flac => "audio/flac",
        Ogg => "audio/ogg",
        _ => "audio/mp4"
    };
}
