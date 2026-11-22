using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyCD.Plugin.FileFormat.CDCollect.Parsing;

/// <summary>
/// Extracts and parses metadata from CDcollect catalog entries.
/// </summary>
public static class CdCollectMetadataExtractor
{
    private static readonly Regex KeyValueRegex = new(
        @"^(?<key>[A-Za-z0-9_\s\-\/]+?)\s*[:=]\s*(?<val>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex DimensionRegex = new(
        @"\b(?<w>\d{2,5})\s*[xX×]\s*(?<h>\d{2,5})\b",
        RegexOptions.Compiled);

    private static readonly Regex DurationRegex = new(
        @"\b(?:\d{1,2}:)?\d{1,2}:\d{2}\b",
        RegexOptions.Compiled);

    private static readonly Regex BitrateRegex = new(
        @"\b(?<rate>\d+)\s*(?:kbps|kbit|kbit/s|kb/s)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses a year/month/day tuple into a standard ISO-8601 date string, or null if invalid.
    /// </summary>
    public static string? FormatDate(int year, int month, int day)
    {
        if (year <= 0)
        {
            return null;
        }

        try
        {
            var validMonth = Math.Clamp(month, 1, 12);
            var maxDay = DateTime.DaysInMonth(year, validMonth);
            var validDay = Math.Clamp(day <= 0 ? 1 : day, 1, maxDay);
            var date = new DateTime(year, validMonth, validDay, 0, 0, 0, DateTimeKind.Utc);
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Populates properties dictionary from CDcollect info string and heuristics.
    /// </summary>
    public static void ExtractInfoProperties(string? info, IDictionary<string, object?> properties)
    {
        if (string.IsNullOrWhiteSpace(info))
        {
            return;
        }

        properties["Comments"] = info.Trim();
        properties["Info"] = info.Trim();

        var normalizedInfo = info.Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "\n");
        var lines = normalizedInfo.Split(new[] { '\r', '\n', '#', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines)
        {
            var match = KeyValueRegex.Match(line);
            if (match.Success)
            {
                var rawKey = match.Groups["key"].Value.Trim();
                var rawVal = match.Groups["val"].Value.Trim();

                if (string.IsNullOrWhiteSpace(rawKey) || string.IsNullOrWhiteSpace(rawVal))
                {
                    continue;
                }

                var normalizedKey = NormalizePropertyKey(rawKey);
                if (normalizedKey.Equals("Bitrate", StringComparison.OrdinalIgnoreCase))
                {
                    var br = BitrateRegex.Match(rawVal);
                    if (br.Success)
                    {
                        rawVal = br.Groups["rate"].Value;
                    }
                }

                if (!properties.ContainsKey(normalizedKey))
                {
                    properties[normalizedKey] = rawVal;
                }
            }
        }

        if (!properties.ContainsKey("Dimensions"))
        {
            var dimMatch = DimensionRegex.Match(normalizedInfo);
            if (dimMatch.Success)
            {
                properties["Dimensions"] = $"{dimMatch.Groups["w"].Value}x{dimMatch.Groups["h"].Value}";
            }
        }

        if (!properties.ContainsKey("Duration"))
        {
            var durMatch = DurationRegex.Match(normalizedInfo);
            if (durMatch.Success)
            {
                properties["Duration"] = durMatch.Value;
            }
        }

        if (!properties.ContainsKey("Bitrate"))
        {
            var brMatch = BitrateRegex.Match(normalizedInfo);
            if (brMatch.Success)
            {
                properties["Bitrate"] = brMatch.Groups["rate"].Value;
            }
        }
    }

    private static string NormalizePropertyKey(string rawKey)
    {
        var cleaned = rawKey.Replace(" ", "").Replace("_", "").Replace("-", "");
        if (cleaned.Equals("artist", StringComparison.OrdinalIgnoreCase)) return "Artist";
        if (cleaned.Equals("album", StringComparison.OrdinalIgnoreCase)) return "Album";
        if (cleaned.Equals("title", StringComparison.OrdinalIgnoreCase)) return "Title";
        if (cleaned.Equals("year", StringComparison.OrdinalIgnoreCase)) return "Year";
        if (cleaned.Equals("genre", StringComparison.OrdinalIgnoreCase)) return "Genre";
        if (cleaned.Equals("track", StringComparison.OrdinalIgnoreCase)) return "Track";
        if (cleaned.Equals("bitrate", StringComparison.OrdinalIgnoreCase)) return "Bitrate";
        if (cleaned.Equals("duration", StringComparison.OrdinalIgnoreCase) || cleaned.Equals("length", StringComparison.OrdinalIgnoreCase) || cleaned.Equals("gesamtzeit", StringComparison.OrdinalIgnoreCase)) return "Duration";
        if (cleaned.Equals("dimensions", StringComparison.OrdinalIgnoreCase) || cleaned.Equals("resolution", StringComparison.OrdinalIgnoreCase) || cleaned.Equals("aufloesung", StringComparison.OrdinalIgnoreCase)) return "Dimensions";
        if (cleaned.Equals("framerate", StringComparison.OrdinalIgnoreCase)) return "Framerate";
        if (cleaned.Equals("codec", StringComparison.OrdinalIgnoreCase) || cleaned.Equals("videoformat", StringComparison.OrdinalIgnoreCase)) return "VideoFormat";
        if (cleaned.Equals("audioformat", StringComparison.OrdinalIgnoreCase)) return "AudioFormat";
        if (cleaned.Equals("channels", StringComparison.OrdinalIgnoreCase) || cleaned.Equals("kanaele", StringComparison.OrdinalIgnoreCase)) return "Channels";
        if (cleaned.Equals("samplerate", StringComparison.OrdinalIgnoreCase)) return "SampleRate";

        return rawKey;
    }
}
