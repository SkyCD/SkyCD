using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Audio.Exceptions;
using SkyCD.Plugin.Metadata.Audio.Models;

namespace SkyCD.Plugin.Metadata.Audio.Parsing;

/// <summary>
/// Reads an M4A / MP4 audio source by walking the atom tree: <c>mvhd</c> for duration, the audio track's
/// sample entry for codec and stream facts, and the iTunes <c>ilst</c> list for tags and cover art.
/// </summary>
/// <remarks>
/// Container recognition is deliberately separate from codec availability: the atom tree is read whatever
/// the codec inside turns out to be, and an unrecognized sample entry still yields duration and tags with
/// the codec reported as the raw four-character code rather than being treated as a failure.
/// </remarks>
internal static class Mp4Reader
{
    private const int MaxDepth = 12;

    public static async Task<AudioProbe> ReadAsync(
        AudioSourceReader source,
        AudioPluginOptions options,
        CancellationToken cancellationToken)
    {
        var header = await source.ReadBytesAsync(0, 12, cancellationToken).ConfigureAwait(false);
        if (header.Length < 12 || !AudioBinary.StartsWith(header, 4, "ftyp"u8))
        {
            throw new AudioDecodeException("the source does not start with an MP4 file type box");
        }

        var state = new Mp4State(options);
        await WalkAsync(source, 0, source.Length, 0, state, cancellationToken).ConfigureAwait(false);

        if (state.DurationSeconds is null && state.TrackDurationSeconds is null && state.Codec is null)
        {
            // An ftyp box alone is not an audio file; a truncated or media-only MP4 must not read as empty tags.
            throw new AudioDecodeException("the MP4 file has no movie header and no audio sample entry");
        }

        if (state.DurationSeconds is null && state.TrackDurationSeconds is not null)
        {
            state.DurationSeconds = state.TrackDurationSeconds;
        }

        var probe = new AudioProbe
        {
            Container = "mp4",
            Codec = state.Codec,
            DurationSeconds = state.DurationSeconds,
            SampleRateHz = state.SampleRateHz,
            Channels = state.Channels,
            BitDepth = state.BitDepth
        };

        state.ApplyTags(probe);

        if (state.DurationSeconds is > 0)
        {
            probe.BitrateKbps = (int)Math.Round(source.Length * 8.0 / state.DurationSeconds.Value / 1000.0);
        }

        probe.Tags.SetSource(probe.Tags.IsEmpty ? "none" : "itunes-atom");
        return probe;
    }

    private static async Task WalkAsync(
        AudioSourceReader source,
        long start,
        long end,
        int depth,
        Mp4State state,
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

            if (++state.AtomsVisited > state.Options.MaxPictureSearchAtoms)
            {
                return;
            }

            var header = await source.ReadBytesAsync(position, 8, cancellationToken).ConfigureAwait(false);
            if (header.Length < 8 || !AudioBinary.TryU32BE(header, 0, out var size32))
            {
                return;
            }

            var type = header.AsSpan(4, 4).ToArray();
            long headerLength = 8;
            long atomSize = size32;

            if (size32 == 1)
            {
                var largesize = await source.ReadBytesAsync(position + 8, 8, cancellationToken).ConfigureAwait(false);
                if (!AudioBinary.TryU64BE(largesize, 0, out var size64))
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
        AudioSourceReader source,
        byte[] type,
        long payloadStart,
        long payloadEnd,
        int depth,
        Mp4State state,
        CancellationToken cancellationToken)
    {
        if (Matches(type, "moov") || Matches(type, "trak") || Matches(type, "mdia") ||
            Matches(type, "minf") || Matches(type, "stbl") || Matches(type, "udta") ||
            Matches(type, "edts"))
        {
            if (Matches(type, "trak"))
            {
                state.BeginTrack();
            }

            await WalkAsync(source, payloadStart, payloadEnd, depth + 1, state, cancellationToken).ConfigureAwait(false);
            return;
        }

        // 'meta' is a full box: four bytes of version and flags precede its children.
        if (Matches(type, "meta"))
        {
            await WalkAsync(source, payloadStart + 4, payloadEnd, depth + 1, state, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (Matches(type, "ilst"))
        {
            await ReadItemListAsync(source, payloadStart, payloadEnd, depth, state, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var payload = payloadEnd - payloadStart;
        if (payload <= 0)
        {
            return;
        }

        if (Matches(type, "mvhd"))
        {
            await ReadMovieHeaderAsync(source, payloadStart, state, cancellationToken).ConfigureAwait(false);
        }
        else if (Matches(type, "mdhd"))
        {
            await ReadMediaHeaderAsync(source, payloadStart, state, cancellationToken).ConfigureAwait(false);
        }
        else if (Matches(type, "hdlr"))
        {
            await ReadHandlerAsync(source, payloadStart, state, cancellationToken).ConfigureAwait(false);
        }
        else if (Matches(type, "stsd"))
        {
            await ReadSampleDescriptionAsync(source, payloadStart, payloadEnd, state, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task ReadMovieHeaderAsync(
        AudioSourceReader source,
        long payloadStart,
        Mp4State state,
        CancellationToken cancellationToken)
    {
        var payload = await source.ReadBytesAsync(payloadStart, 32, cancellationToken).ConfigureAwait(false);
        if (payload.Length < 12)
        {
            return;
        }

        var version = payload[0];
        uint timescale;
        ulong duration;

        if (version == 1)
        {
            // v1: creation (8), modification (8), timescale (4), duration (8).
            if (!AudioBinary.TryU32BE(payload, 20, out timescale) ||
                !AudioBinary.TryU64BE(payload, 24, out duration))
            {
                return;
            }
        }
        else
        {
            // v0: creation (4), modification (4), timescale (4), duration (4).
            if (!AudioBinary.TryU32BE(payload, 12, out timescale) ||
                !AudioBinary.TryU32BE(payload, 16, out var duration32))
            {
                return;
            }

            duration = duration32;
        }

        if (timescale > 0 && duration > 0)
        {
            state.DurationSeconds = (double)duration / timescale;
        }
    }

    private static async Task ReadMediaHeaderAsync(
        AudioSourceReader source,
        long payloadStart,
        Mp4State state,
        CancellationToken cancellationToken)
    {
        var payload = await source.ReadBytesAsync(payloadStart, 32, cancellationToken).ConfigureAwait(false);
        if (payload.Length < 12)
        {
            return;
        }

        var version = payload[0];
        uint timescale;
        ulong duration;

        if (version == 1)
        {
            if (!AudioBinary.TryU32BE(payload, 20, out timescale) ||
                !AudioBinary.TryU64BE(payload, 24, out duration))
            {
                return;
            }
        }
        else
        {
            if (!AudioBinary.TryU32BE(payload, 12, out timescale) ||
                !AudioBinary.TryU32BE(payload, 16, out var duration32))
            {
                return;
            }

            duration = duration32;
        }

        if (timescale > 0 && duration > 0)
        {
            state.TrackTimescale = timescale;
            state.TrackDurationSeconds = (double)duration / timescale;
        }
    }

    private static async Task ReadHandlerAsync(
        AudioSourceReader source,
        long payloadStart,
        Mp4State state,
        CancellationToken cancellationToken)
    {
        // hdlr: version and flags (4), pre-defined (4), handler type (4).
        var payload = await source.ReadBytesAsync(payloadStart + 8, 4, cancellationToken).ConfigureAwait(false);
        state.IsAudioTrack = Matches(payload, "soun");
    }

    private static async Task ReadSampleDescriptionAsync(
        AudioSourceReader source,
        long payloadStart,
        long payloadEnd,
        Mp4State state,
        CancellationToken cancellationToken)
    {
        if (!state.IsAudioTrack)
        {
            return;
        }

        // stsd: version and flags (4), entry count (4), then the entries.
        var counts = await source.ReadBytesAsync(payloadStart, 8, cancellationToken).ConfigureAwait(false);
        if (counts.Length < 8 || !AudioBinary.TryU32BE(counts, 4, out var entryCount) || entryCount == 0)
        {
            return;
        }

        var entryStart = payloadStart + 8;
        if (entryStart + 36 > payloadEnd)
        {
            return;
        }

        var entry = await source.ReadBytesAsync(entryStart, 36, cancellationToken).ConfigureAwait(false);
        if (entry.Length < 36 || !AudioBinary.TryU32BE(entry, 0, out _))
        {
            return;
        }

        state.Codec = ResolveCodec(Encoding.ASCII.GetString(entry, 4, 4));

        // AudioSampleEntry: 6 reserved, 2 data reference index, 8 reserved, then channel count, sample
        // size, and a 16.16 fixed-point sample rate.
        if (AudioBinary.TryU16BE(entry, 24, out var channels) && channels > 0)
        {
            state.Channels = channels;
        }

        if (AudioBinary.TryU16BE(entry, 26, out var sampleSize) && sampleSize > 0)
        {
            state.BitDepth = sampleSize;
        }

        if (AudioBinary.TryU32BE(entry, 32, out var sampleRateFixed) && sampleRateFixed > 0)
        {
            state.SampleRateHz = (int)(sampleRateFixed >> 16);
        }
    }

    private static string ResolveCodec(string fourCc) => fourCc switch
    {
        "mp4a" => "aac",
        "alac" => "alac",
        "ac-3" => "ac3",
        "ec-3" => "eac3",
        "dtsc" or "dtsh" or "dtsl" => "dts",
        "flac" => "flac",
        "Opus" or "opus" => "opus",
        "samr" => "amr-nb",
        "sawb" => "amr-wb",
        "sowt" or "twos" => "pcm",
        _ => fourCc.Trim().ToLowerInvariant()
    };

    private static async Task ReadItemListAsync(
        AudioSourceReader source,
        long start,
        long end,
        int depth,
        Mp4State state,
        CancellationToken cancellationToken)
    {
        var position = start;
        while (position + 8 <= end)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (++state.AtomsVisited > state.Options.MaxPictureSearchAtoms)
            {
                return;
            }

            var header = await source.ReadBytesAsync(position, 8, cancellationToken).ConfigureAwait(false);
            if (header.Length < 8 || !AudioBinary.TryU32BE(header, 0, out var size32))
            {
                return;
            }

            long atomSize = size32;
            long headerLength = 8;
            if (size32 == 1)
            {
                var largesize = await source.ReadBytesAsync(position + 8, 8, cancellationToken).ConfigureAwait(false);
                if (!AudioBinary.TryU64BE(largesize, 0, out var size64))
                {
                    return;
                }

                atomSize = (long)size64;
                headerLength = 16;
            }
            else if (size32 == 0)
            {
                atomSize = end - position;
            }

            if (atomSize < headerLength || position + atomSize > end)
            {
                return;
            }

            await ReadItemAsync(source, header.AsSpan(4, 4).ToArray(), position + headerLength, position + atomSize, state,
                cancellationToken).ConfigureAwait(false);

            position += atomSize;
        }
    }

    private static async Task ReadItemAsync(
        AudioSourceReader source,
        byte[] key,
        long payloadStart,
        long payloadEnd,
        Mp4State state,
        CancellationToken cancellationToken)
    {
        var payload = payloadEnd - payloadStart;
        if (payload < 16)
        {
            return;
        }

        // Every item holds a 'data' child; its flags say whether the payload is text, an integer or a picture.
        var dataHeader = await source.ReadBytesAsync(payloadStart, 16, cancellationToken).ConfigureAwait(false);
        if (dataHeader.Length < 16 || !AudioBinary.StartsWith(dataHeader, 4, "data"u8) ||
            !AudioBinary.TryU32BE(dataHeader, 0, out var dataSize) || dataSize < 16)
        {
            return;
        }

        var valueLength = (int)Math.Min(dataSize - 16, payloadEnd - payloadStart - 16);
        if (valueLength <= 0)
        {
            return;
        }

        var flags = dataHeader[11];
        var value = await source.ReadBytesAsync(payloadStart + 16, valueLength, cancellationToken)
            .ConfigureAwait(false);

        state.Add(key, flags, value);
    }

    private static bool Matches(ReadOnlySpan<byte> type, string fourCc) =>
        type.Length >= 4 &&
        type[0] == (byte)fourCc[0] && type[1] == (byte)fourCc[1] &&
        type[2] == (byte)fourCc[2] && type[3] == (byte)fourCc[3];

    /// <summary>Mutable state carried through one atom walk.</summary>
    private sealed class Mp4State
    {
        private readonly System.Collections.Generic.Dictionary<string, string> text =
            new(StringComparer.Ordinal);

        public Mp4State(AudioPluginOptions options)
        {
            Options = options;
        }

        public AudioPluginOptions Options { get; }
        public int AtomsVisited { get; set; }

        // Settable from the enclosing reader, which walks the atom tree and fills this in as it goes.
        public bool IsAudioTrack { get; set; }
        public double? DurationSeconds { get; set; }
        public double? TrackDurationSeconds { get; set; }
        public uint TrackTimescale { get; set; }
        public string? Codec { get; set; }
        public int? SampleRateHz { get; set; }
        public int? Channels { get; set; }
        public int? BitDepth { get; set; }
        public AudioPicture? Picture { get; private set; }

        public void BeginTrack()
        {
            IsAudioTrack = false;
            TrackDurationSeconds = null;
            TrackTimescale = 0;
        }

        public void Add(byte[] key, byte flags, byte[] value)
        {
            if (Matches(key, "covr"))
            {
                // Flags 13 and 14 mean JPEG and PNG; anything else is sniffed from the bytes.
                var contentType = flags switch
                {
                    13 => "image/jpeg",
                    14 => "image/png",
                    _ => OggReader.SniffImageContentType(value)
                };

                Picture ??= new AudioPicture(contentType, value) { IsFrontCover = true };
                return;
            }

            if (Matches(key, "trkn") || Matches(key, "disk"))
            {
                // Pairs are stored as 16-bit fields with a two-byte prefix.
                if (value.Length >= 6 && AudioBinary.TryU16BE(value, 2, out var number))
                {
                    AudioBinary.TryU16BE(value, 4, out var total);
                    if (Matches(key, "trkn"))
                    {
                        TrackNumber = NonZero(number);
                        TrackCount = NonZero(total);
                    }
                    else
                    {
                        DiscNumber = NonZero(number);
                        DiscCount = NonZero(total);
                    }
                }

                return;
            }

            if (Matches(key, "gnre") && flags == 0 && value.Length >= 2)
            {
                // A binary genre is a one-based index into the ID3v1 list.
                if (AudioBinary.TryU16BE(value, 0, out var genreIndex) && genreIndex >= 1 && genreIndex <= Id3Genres.All.Length)
                {
                    text.TryAdd("genre", Id3Genres.All[genreIndex - 1]);
                }

                return;
            }

            if (flags != 1)
            {
                // Not UTF-8 text; integers and binaries other than the ones handled above are not tags.
                return;
            }

            var decoded = Encoding.UTF8.GetString(value).TrimEnd('\0').Trim();
            if (decoded.Length == 0)
            {
                return;
            }

            var name = ResolveKeyName(key);
            if (name is not null)
            {
                text[name] = text.TryGetValue(name, out var existing)
                    ? AudioTags.Merge(existing, decoded)!
                    : decoded;
            }
        }

        public int? TrackNumber { get; private set; }
        public int? TrackCount { get; private set; }
        public int? DiscNumber { get; private set; }
        public int? DiscCount { get; private set; }

        public void ApplyTags(AudioProbe probe)
        {
            probe.Tags.AddTitle(Lookup("title"));
            probe.Tags.AddArtist(Lookup("artist"));
            probe.Tags.AddAlbum(Lookup("album"));
            probe.Tags.AddAlbumArtist(Lookup("albumartist"));
            probe.Tags.AddGenre(Lookup("genre"));
            probe.Tags.SetTrack(TrackNumber, TrackCount);
            probe.Tags.SetDisc(DiscNumber, DiscCount);
            probe.Tags.SetYear(AudioBinary.ParseYear(Lookup("date")));

            if (Picture is not null)
            {
                probe.Pictures.Add(Picture);
            }
        }

        private string? Lookup(string name) => text.TryGetValue(name, out var value) ? value : null;

        private static int? NonZero(ushort value) => value == 0 ? null : value;

        /// <summary>
        /// Maps an iTunes item key to a stable name. Copyrighted keys start with 0xA9, which is not ASCII, so
        /// the leading byte is matched separately from the printable remainder.
        /// </summary>
        private static string? ResolveKeyName(byte[] key)
        {
            if (key.Length < 4)
            {
                return null;
            }

            var tail = Encoding.ASCII.GetString(key, 1, 3);
            return key[0] == 0xA9
                ? tail switch
                {
                    "nam" => "title",
                    "ART" => "artist",
                    "alb" => "album",
                    "gen" => "genre",
                    "day" => "date",
                    "cmt" => null,
                    _ => null
                }
                : Encoding.ASCII.GetString(key, 0, 4) switch
                {
                    "aART" => "albumartist",
                    "gnre" => "genre",
                    _ => null
                };
        }
    }
}
