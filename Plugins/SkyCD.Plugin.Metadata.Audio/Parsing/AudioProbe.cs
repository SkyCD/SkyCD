using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Metadata.Audio.Parsing;

/// <summary>
/// Normalized tag values collected from whichever tagging scheme the container uses. Multi-value tags are
/// merged into one display string rather than dropped or arbitrarily picked, because the host schema stores
/// one value per key.
/// </summary>
internal sealed class AudioTags
{
    /// <summary>Separator used when a container repeats a field or a frame carries several values.</summary>
    internal const string MultiValueSeparator = "; ";

    public string? Title { get; private set; }
    public string? Artist { get; private set; }
    public string? Album { get; private set; }
    public string? AlbumArtist { get; private set; }
    public string? Genre { get; private set; }
    public int? TrackNumber { get; private set; }
    public int? TrackCount { get; private set; }
    public int? DiscNumber { get; private set; }
    public int? DiscCount { get; private set; }
    public int? Year { get; private set; }

    /// <summary>Which tagging scheme produced these values, published as provenance.</summary>
    public string? Source { get; private set; }

    public bool IsEmpty =>
        Title is null && Artist is null && Album is null && AlbumArtist is null && Genre is null &&
        TrackNumber is null && DiscNumber is null && Year is null;

    public void AddTitle(string? value) => Title = Merge(Title, value);

    public void AddArtist(string? value) => Artist = Merge(Artist, value);

    public void AddAlbum(string? value) => Album = Merge(Album, value);

    public void AddAlbumArtist(string? value) => AlbumArtist = Merge(AlbumArtist, value);

    public void AddGenre(string? value) => Genre = Merge(Genre, value);

    public void SetTrack(int? number, int? count)
    {
        TrackNumber ??= number;
        TrackCount ??= count;
    }

    public void SetDisc(int? number, int? count)
    {
        DiscNumber ??= number;
        DiscCount ??= count;
    }

    public void SetYear(int? year) => Year ??= year;

    public void SetSource(string? source) => Source ??= source;

    /// <summary>
    /// Joins a repeated or already populated field with the next distinct value. An existing value wins over
    /// a duplicate, so reading ID3v2 after ID3v1 refines rather than replaces what was found.
    /// </summary>
    internal static string? Merge(string? existing, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
        {
            return existing;
        }

        var trimmed = incoming.Trim();
        if (trimmed.Length == 0)
        {
            return existing;
        }

        if (string.IsNullOrWhiteSpace(existing))
        {
            return trimmed;
        }

        foreach (var part in existing.Split(MultiValueSeparator, StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, trimmed, StringComparison.Ordinal))
            {
                return existing;
            }
        }

        return existing + MultiValueSeparator + trimmed;
    }
}

/// <summary>Embedded cover art as stored, with the MIME type the container declared.</summary>
internal sealed record AudioPicture(string ContentType, byte[] Data)
{
    /// <summary>Front cover is what a browser should show when several pictures are embedded.</summary>
    public bool IsFrontCover { get; init; }
}

/// <summary>Everything the plugin learned about one source, in the terms the schema publishes.</summary>
internal sealed class AudioProbe
{
    /// <summary>Container detected from the bytes, not the file name: <c>mp3</c>, <c>flac</c>, <c>ogg</c> or <c>mp4</c>.</summary>
    public required string Container { get; init; }

    public string? Codec { get; set; }
    public double? DurationSeconds { get; set; }
    public int? BitrateKbps { get; set; }
    public int? SampleRateHz { get; set; }
    public int? Channels { get; set; }
    public int? BitDepth { get; set; }
    public AudioTags Tags { get; } = new();
    public List<AudioPicture> Pictures { get; } = [];

    /// <summary>The picture a browser should show: the declared front cover, else the first one found.</summary>
    public AudioPicture? CoverArt
    {
        get
        {
            foreach (var picture in Pictures)
            {
                if (picture.IsFrontCover)
                {
                    return picture;
                }
            }

            return Pictures.Count > 0 ? Pictures[0] : null;
        }
    }
}
