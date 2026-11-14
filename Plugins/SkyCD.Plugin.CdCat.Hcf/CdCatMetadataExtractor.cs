using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyCD.Plugin.CdCat.Hcf;

/// <summary>
/// Extracts and normalizes audio, video, image, and loan metadata from CdCat XML elements and comments.
/// </summary>
public static partial class CdCatMetadataExtractor
{
    public static void ExtractMp3Tag(
        IDictionary<string, string> attributes,
        string? innerComment,
        IDictionary<string, object?> properties)
    {
        if (attributes.TryGetValue("album", out var album) && !string.IsNullOrWhiteSpace(album))
        {
            properties["Album"] = album.Trim();
        }

        if (attributes.TryGetValue("artist", out var artist) && !string.IsNullOrWhiteSpace(artist))
        {
            properties["Artist"] = artist.Trim();
        }

        if (attributes.TryGetValue("title", out var title) && !string.IsNullOrWhiteSpace(title))
        {
            properties["Title"] = title.Trim();
        }

        if (attributes.TryGetValue("year", out var year) && !string.IsNullOrWhiteSpace(year))
        {
            properties["Year"] = year.Trim();
        }

        if (attributes.TryGetValue("genre", out var genre) && !string.IsNullOrWhiteSpace(genre))
        {
            properties["Genre"] = genre.Trim();
        }

        if (attributes.TryGetValue("track", out var track) && !string.IsNullOrWhiteSpace(track))
        {
            properties["Track"] = track.Trim();
        }

        if (attributes.TryGetValue("bitrate", out var bitrate) && !string.IsNullOrWhiteSpace(bitrate))
        {
            properties["Bitrate"] = bitrate.Trim();
        }

        if (attributes.TryGetValue("duration", out var duration) && !string.IsNullOrWhiteSpace(duration))
        {
            properties["Duration"] = duration.Trim();
        }

        if (!string.IsNullOrWhiteSpace(innerComment))
        {
            var trimmedInner = innerComment.Trim();
            if (!properties.ContainsKey("Comments"))
            {
                properties["Comments"] = trimmedInner;
            }
            else
            {
                properties["AudioComment"] = trimmedInner;
            }
        }
    }

    public static void ExtractCommentMetadata(
        string? commentText,
        IDictionary<string, object?> properties)
    {
        if (string.IsNullOrWhiteSpace(commentText))
        {
            return;
        }

        var trimmed = commentText.Trim();
        properties["Comments"] = trimmed;

        try
        {
            if (trimmed.StartsWith("Video:#", StringComparison.OrdinalIgnoreCase))
            {
                ParseVideoHashComment(trimmed, properties);
            }
            else if (VideoTracksRegex().IsMatch(trimmed))
            {
                ParseVideoTracksComment(trimmed, properties);
            }
            else if (QuotedVideoRegex().IsMatch(trimmed))
            {
                ParseQuotedVideoComment(trimmed, properties);
            }
            else if (ImageRegex().IsMatch(trimmed))
            {
                ParseImageComment(trimmed, properties);
            }
            else if (AudioDurationRegex().Match(trimmed) is { Success: true } audioMatch)
            {
                properties["Duration"] = audioMatch.Groups[1].Value.Trim();
            }
        }
        catch
        {
            // Fallback gracefully without losing raw comment
        }
    }

    private static void ParseVideoHashComment(string text, IDictionary<string, object?> properties)
    {
        var streams = text.Split(["##"], StringSplitOptions.RemoveEmptyEntries);
        var videoStream = streams.Length > 0 ? streams[0] : text;

        string? videoFormat = null;
        string? framerate = null;

        var fpsMatch = FpsRegex().Match(videoStream);
        if (fpsMatch.Success && float.TryParse(fpsMatch.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fps))
        {
            framerate = string.Format(CultureInfo.InvariantCulture, "{0:F2} fps", fps);
            properties["Framerate"] = framerate;
        }

        var formatMatch = VideoHeaderFormatRegex().Match(videoStream);
        if (formatMatch.Success)
        {
            videoFormat = formatMatch.Groups[1].Value;
            if (framerate is not null)
            {
                videoFormat += ", " + framerate;
            }
            properties["VideoFormat"] = videoFormat;
        }

        var durationMatch = DurationRegex().Match(videoStream);
        if (durationMatch.Success)
        {
            properties["Duration"] = durationMatch.Groups[1].Value;
        }

        var resMatch = ResolutionRegex().Match(videoStream);
        if (resMatch.Success)
        {
            properties["Dimensions"] = resMatch.Groups[1].Value;
        }

        if (streams.Length > 1 && streams[1].StartsWith("Audio:#", StringComparison.OrdinalIgnoreCase))
        {
            var audioStream = streams[1];
            var audioParts = new List<string>();

            var freqMatch = FrequencyRegex().Match(audioStream);
            if (freqMatch.Success && float.TryParse(freqMatch.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var freq))
            {
                var freqHz = (int)freq * 1000;
                audioParts.Add($"{freqHz} Hz");
            }

            var bitMatch = BitrateRegex().Match(audioStream);
            if (bitMatch.Success)
            {
                audioParts.Add($"{bitMatch.Groups[1].Value} kb/s");
            }

            var audioFormatMatch = AudioHeaderFormatRegex().Match(audioStream);
            var audioFormat = audioFormatMatch.Success ? audioFormatMatch.Groups[1].Value : null;

            if (audioFormat is not null)
            {
                if (audioParts.Count > 0)
                {
                    audioFormat += ", " + string.Join(", ", audioParts);
                }
                properties["AudioFormat"] = audioFormat;
            }

            if (videoFormat is not null && audioFormat is not null)
            {
                properties["Format"] = $"Video: {videoFormat}; Audio: {audioFormat}";
            }
            else if (videoFormat is not null)
            {
                properties["Format"] = videoFormat;
            }
        }
        else if (videoFormat is not null)
        {
            properties["Format"] = videoFormat;
        }
    }

    private static void ParseVideoTracksComment(string text, IDictionary<string, object?> properties)
    {
        var resMatch = ResolutionRegex().Match(text);
        if (resMatch.Success)
        {
            properties["Dimensions"] = resMatch.Groups[1].Value;
        }

        var durationMatch = DurationRegex().Match(text);
        if (durationMatch.Success)
        {
            properties["Duration"] = durationMatch.Groups[1].Value;
        }
        else
        {
            var minMatch = MinutesDurationRegex().Match(text);
            if (minMatch.Success)
            {
                properties["Duration"] = minMatch.Groups[1].Value;
            }
        }
    }

    private static void ParseQuotedVideoComment(string text, IDictionary<string, object?> properties)
    {
        var match = QuotedVideoRegex().Match(text);
        if (match.Success)
        {
            properties["Dimensions"] = match.Groups[1].Value.Replace(" ", string.Empty);
        }

        var durationMatch = DurationRegex().Match(text);
        if (durationMatch.Success)
        {
            properties["Duration"] = durationMatch.Groups[1].Value;
        }
    }

    private static void ParseImageComment(string text, IDictionary<string, object?> properties)
    {
        var match = ImageRegex().Match(text);
        if (match.Success)
        {
            properties["Dimensions"] = match.Groups[1].Value.Replace(" ", string.Empty);
        }
    }

    [GeneratedRegex(@"([\d\.]+) f/s", RegexOptions.Compiled)]
    private static partial Regex FpsRegex();

    [GeneratedRegex(@"^Video:#([#/\w\.\-\+\(\)\$\s]+)#", RegexOptions.Compiled)]
    private static partial Regex VideoHeaderFormatRegex();

    [GeneratedRegex(@"^Audio:#([#/\w\.\-\+\(\)\$\s]+)#", RegexOptions.Compiled)]
    private static partial Regex AudioHeaderFormatRegex();

    [GeneratedRegex(@"(\d+:\d+:\d+)", RegexOptions.Compiled)]
    private static partial Regex DurationRegex();

    [GeneratedRegex(@"(\d+:\d+)\s*min", RegexOptions.Compiled)]
    private static partial Regex MinutesDurationRegex();

    [GeneratedRegex(@"(\d+x\d+)", RegexOptions.Compiled)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"([\w\.\s]+)kHz", RegexOptions.Compiled)]
    private static partial Regex FrequencyRegex();

    [GeneratedRegex(@"(\d+)\s*kBit", RegexOptions.Compiled)]
    private static partial Regex BitrateRegex();

    [GeneratedRegex(@"Video tracks:\r?\nTrack \d+:", RegexOptions.Compiled)]
    private static partial Regex VideoTracksRegex();

    [GeneratedRegex(@"^""?(\d+\s*x\s*\d+),\s*\d+[,\.]\d+\s*fps", RegexOptions.Compiled)]
    private static partial Regex QuotedVideoRegex();

    [GeneratedRegex(@"^""?(\d+\s*x\s*\d+),\s*([\w\.\s]+colors|Black and White)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"^(\d+:\d{1,2}),\s*\d+\s*kbps", RegexOptions.Compiled)]
    private static partial Regex AudioDurationRegex();
}
