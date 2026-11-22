using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.FileFormat.BinCue.Exceptions;

namespace SkyCD.Plugin.FileFormat.BinCue.Models;

/// <summary>
/// Byte layout of a track mode used by BIN/CUE images. The BIN file stores fixed-size sectors and only
/// the 2048-byte user-data area of every sector belongs to the ISO 9660 filesystem.
/// </summary>
internal sealed record CueTrackMode(int SectorSize, int DataOffsetInSector, bool IsData)
{
    public static CueTrackMode? TryParse(string? mode)
    {
        return mode?.Trim().ToUpperInvariant() switch
        {
            "AUDIO" => new CueTrackMode(2352, 0, IsData: false),
            "MODE1/2048" => new CueTrackMode(2048, 0, IsData: true),
            "MODE1/2352" => new CueTrackMode(2352, 16, IsData: true),
            "MODE2/2048" => new CueTrackMode(2048, 0, IsData: true),
            "MODE2/2336" => new CueTrackMode(2336, 8, IsData: true),
            "MODE2/2352" => new CueTrackMode(2352, 24, IsData: true),
            "CDI/2336" => new CueTrackMode(2336, 8, IsData: true),
            "CDI/2352" => new CueTrackMode(2352, 24, IsData: true),
            _ => null
        };
    }
}

/// <summary>One FILE statement in a CUE sheet.</summary>
internal sealed record CueFile(string Name, string Type);

/// <summary>One TRACK statement in a CUE sheet, attached to the FILE that declares it.</summary>
internal sealed record CueTrack(int Number, CueTrackMode? Mode, long? Index1, int FileIndex);

/// <summary>
/// Parsed contents of a CUE sheet. Only the FILE, TRACK, and INDEX statements needed to locate and size
/// the data track are retained; audio metadata, pregaps, and catalog data are ignored.
/// </summary>
internal sealed record CueSheet(IReadOnlyList<CueFile> Files, IReadOnlyList<CueTrack> Tracks)
{
    /// <summary>CUE sheets are small text files; anything larger is rejected as damaged.</summary>
    public const int MaxSheetBytes = 1 << 20;

    private const int MaxTracks = 1000;
    private const int MaxFiles = 100;

    /// <summary>Reads at most <see cref="MaxSheetBytes"/> plus one byte, so an oversized sheet is rejected as damaged.</summary>
    public static async Task<CueSheet> ReadAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var buffer = new byte[MaxSheetBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length &&
               (read = await source.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
        }

        return Parse(buffer.AsSpan(0, total));
    }

    public static CueSheet Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0)
        {
            throw new BinCueImageFormatException("the CUE sheet is empty.");
        }

        if (content.Length > MaxSheetBytes)
        {
            throw new BinCueImageFormatException("the CUE sheet is too large.");
        }

        if (content.IndexOf((byte)0) >= 0)
        {
            throw new BinCueImageFormatException("the CUE sheet is not readable text.");
        }

        var text = Decode(content);
        var files = new List<CueFile>();
        var tracks = new List<CueTrack>();
        var currentTrack = (CueTrackBuilder?)null;
        var currentFile = -1;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var command = ReadCommand(line, out var arguments);
            switch (command)
            {
                case "FILE":
                    FlushTrack(tracks, currentTrack);
                    currentTrack = null;
                    if (files.Count >= MaxFiles)
                    {
                        throw new BinCueImageFormatException("the CUE sheet lists too many files.");
                    }

                    files.Add(ParseFile(arguments));
                    currentFile = files.Count - 1;
                    break;

                case "TRACK":
                    if (currentFile < 0)
                    {
                        throw new BinCueImageFormatException("a track is declared before any file.");
                    }

                    FlushTrack(tracks, currentTrack);
                    if (tracks.Count >= MaxTracks)
                    {
                        throw new BinCueImageFormatException("the CUE sheet lists too many tracks.");
                    }

                    currentTrack = ParseTrack(arguments, currentFile);
                    break;

                case "INDEX":
                    if (currentTrack is not null)
                    {
                        ParseIndex(arguments, currentTrack);
                    }

                    break;
            }
        }

        FlushTrack(tracks, currentTrack);
        if (files.Count == 0 || tracks.Count == 0)
        {
            throw new BinCueImageFormatException("the CUE sheet lists no tracks.");
        }

        return new CueSheet(files, tracks);
    }

    private static string ReadCommand(string line, out string arguments)
    {
        var separator = line.IndexOfAny([' ', '\t']);
        if (separator < 0)
        {
            arguments = string.Empty;
            return line.ToUpperInvariant();
        }

        arguments = line[(separator + 1)..].Trim();
        return line[..separator].ToUpperInvariant();
    }

    private static CueFile ParseFile(string arguments)
    {
        var name = ReadArgument(arguments, out var remainder);
        var type = remainder.Trim();
        if (name.Length == 0 || type.Length == 0)
        {
            throw new BinCueImageFormatException("a FILE statement is incomplete.");
        }

        return new CueFile(name, type.ToUpperInvariant());
    }

    private static CueTrackBuilder ParseTrack(string arguments, int fileIndex)
    {
        var numberText = ReadArgument(arguments, out var remainder);
        if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number <= 0 || number > 99)
        {
            throw new BinCueImageFormatException("a TRACK number is invalid.");
        }

        var modeText = remainder.Trim();
        if (modeText.Length == 0)
        {
            throw new BinCueImageFormatException("a TRACK statement has no mode.");
        }

        return new CueTrackBuilder(number, CueTrackMode.TryParse(modeText), fileIndex);
    }

    private static void ParseIndex(string arguments, CueTrackBuilder track)
    {
        var numberText = ReadArgument(arguments, out var remainder);
        if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number < 0 || number > 99)
        {
            throw new BinCueImageFormatException("an INDEX number is invalid.");
        }

        var stamp = remainder.Trim();
        var frames = ParseStamp(stamp);
        if (number == 1 && track.Index1 is null)
        {
            track.Index1 = frames;
        }
    }

    /// <summary>Parses a CUE <c>mm:ss:ff</c> time stamp, where the last field counts 75 frames per second.</summary>
    private static long ParseStamp(string stamp)
    {
        var parts = stamp.Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var frames)
            || minutes < 0 || seconds is < 0 or >= 60 || frames is < 0 or >= 75)
        {
            throw new BinCueImageFormatException("an INDEX time stamp is invalid.");
        }

        return ((long)minutes * 60 + seconds) * 75 + frames;
    }

    /// <summary>Reads a quoted or space-delimited first argument and returns the rest of the line.</summary>
    private static string ReadArgument(string arguments, out string remainder)
    {
        var text = arguments.TrimStart();
        if (text.Length == 0)
        {
            remainder = string.Empty;
            return string.Empty;
        }

        if (text[0] == '"')
        {
            var closing = text.IndexOf('"', 1);
            if (closing < 0)
            {
                throw new BinCueImageFormatException("a quoted file name is not terminated.");
            }

            remainder = text[(closing + 1)..].Trim();
            return text[1..closing];
        }

        var separator = text.IndexOfAny([' ', '\t']);
        if (separator < 0)
        {
            remainder = string.Empty;
            return text;
        }

        remainder = text[(separator + 1)..].Trim();
        return text[..separator];
    }

    private static void FlushTrack(List<CueTrack> tracks, CueTrackBuilder? builder)
    {
        if (builder is not null)
        {
            tracks.Add(new CueTrack(builder.Number, builder.Mode, builder.Index1, builder.FileIndex));
        }
    }

    private static string Decode(ReadOnlySpan<byte> content)
    {
        try
        {
            var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(content);
            return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    private sealed class CueTrackBuilder(int number, CueTrackMode? mode, int fileIndex)
    {
        public int Number { get; } = number;

        public CueTrackMode? Mode { get; } = mode;

        public long? Index1 { get; set; }

        public int FileIndex { get; } = fileIndex;
    }
}
