using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Metadata.DiskImages.Ccd.Exceptions;

namespace SkyCD.Plugin.Metadata.DiskImages.Ccd.Parsing;

/// <summary>
/// Byte layout of a track mode used by CloneCD. The track is stored as fixed-size sectors and only the
/// 2048-byte user-data area of every sector belongs to the ISO 9660 filesystem.
/// </summary>
internal sealed record CcdTrackMode(int SectorSize, int DataOffsetInSector, bool IsData)
{
    public static CcdTrackMode? TryParse(string? mode)
    {
        return mode?.Trim().ToUpperInvariant() switch
        {
            "AUDIO" => new CcdTrackMode(2352, 0, IsData: false),
            "MODE1/2048" => new CcdTrackMode(2048, 0, IsData: true),
            "MODE1/2352" => new CcdTrackMode(2352, 16, IsData: true),
            "MODE2/2048" => new CcdTrackMode(2048, 0, IsData: true),
            "MODE2/2336" => new CcdTrackMode(2336, 8, IsData: true),
            "MODE2/2352" => new CcdTrackMode(2352, 24, IsData: true),
            _ => null
        };
    }
}

/// <summary>One track declared by a CCD descriptor.</summary>
internal sealed record CcdTrack(int Number, CcdTrackMode? Mode, long? Index1);

/// <summary>
/// Parsed contents of a CloneCD descriptor. Only the INI sections needed to locate and size the data
/// track are retained; track entries, session data, and audio metadata are ignored.
/// </summary>
internal sealed record CcdDescriptor(string? ImageFileName, bool DataTracksScrambled, IReadOnlyList<CcdTrack> Tracks)
{
    /// <summary>Descriptors are small INI files; anything larger is rejected as damaged.</summary>
    public const int MaxDescriptorBytes = 1 << 20;

    private const int MaxTracks = 100;

    public static CcdDescriptor Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0)
        {
            throw new CcdImageFormatException("the descriptor is empty.");
        }

        if (content.Length > MaxDescriptorBytes)
        {
            throw new CcdImageFormatException("the descriptor is too large.");
        }

        if (content.IndexOf((byte)0) >= 0)
        {
            throw new CcdImageFormatException("the descriptor is not readable text.");
        }

        var text = System.Text.Encoding.UTF8.GetString(content);
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var tracks = new SortedDictionary<int, TrackBuilder>();
        string? imageFileName = null;
        var scrambled = false;
        string section = string.Empty;
        var sawSection = false;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim().ToUpperInvariant();
                sawSection = true;
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                if (sawSection)
                {
                    throw new CcdImageFormatException("the descriptor contains a line that is not a key/value pair.");
                }

                continue;
            }

            var key = line[..separator].Trim().ToUpperInvariant();
            var value = line[(separator + 1)..].Trim();

            if (section == "DISC")
            {
                if (key == "IMAGEFILE" && value.Length > 0)
                {
                    imageFileName = value;
                }
                else if (key == "DATATRACKSSCRAMBLED" && value == "1")
                {
                    scrambled = true;
                }

                continue;
            }

            if (!TryParseTrackNumber(section, out var trackNumber))
            {
                continue;
            }

            if (!tracks.TryGetValue(trackNumber, out var track))
            {
                if (tracks.Count >= MaxTracks)
                {
                    throw new CcdImageFormatException("the descriptor lists too many tracks.");
                }

                track = new TrackBuilder();
                tracks.Add(trackNumber, track);
            }

            if (key == "MODE")
            {
                track.Mode = CcdTrackMode.TryParse(value);
            }
            else if (key == "INDEX 1")
            {
                if (!long.TryParse(value, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var index) || index < 0)
                {
                    throw new CcdImageFormatException("a track index is invalid.");
                }

                track.Index1 = index;
            }
        }

        if (!sawSection || tracks.Count == 0)
        {
            throw new CcdImageFormatException("the descriptor lists no tracks.");
        }

        return new CcdDescriptor(
            string.IsNullOrWhiteSpace(imageFileName) ? null : imageFileName,
            scrambled,
            tracks.Select(pair => new CcdTrack(pair.Key, pair.Value.Mode, pair.Value.Index1)).ToArray());
    }

    private static bool TryParseTrackNumber(string section, out int trackNumber)
    {
        trackNumber = 0;
        if (!section.StartsWith("TRACK", StringComparison.Ordinal))
        {
            return false;
        }

        var number = section[5..].Trim();
        return number.Length > 0 &&
               int.TryParse(number, System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture, out trackNumber);
    }

    private sealed class TrackBuilder
    {
        public CcdTrackMode? Mode { get; set; }

        public long? Index1 { get; set; }
    }
}
