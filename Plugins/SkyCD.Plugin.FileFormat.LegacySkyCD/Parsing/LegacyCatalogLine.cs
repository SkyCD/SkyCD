using System;
using System.Text.RegularExpressions;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;

namespace SkyCD.Plugin.FileFormat.LegacySkyCD.Parsing;

/// <summary>Line grammar shared by the legacy text and compressed-text catalogs.</summary>
internal static class LegacyCatalogLine
{
    private static readonly Regex SizePrefix = new(@"^\[(?<size>[^\]]+)\]\s*(?<path>.*)$", RegexOptions.Compiled);

    public static bool IsIgnored(string line) =>
        string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal);

    public static LegacyCatalogLineEntry Parse(string path)
    {
        var segmentStart = path.LastIndexOf('\\') + 1;
        var sizeMatch = SizePrefix.Match(path[segmentStart..]);
        if (sizeMatch.Success && LegacyScdSize.TryParse(sizeMatch.Groups["size"].Value, out var parsedBytes))
        {
            return new LegacyCatalogLineEntry(path[..segmentStart] + sizeMatch.Groups["path"].Value.Trim(), parsedBytes,
                false);
        }

        var prefix = SizePrefix.Match(path);
        if (prefix.Success && LegacyScdSize.TryParse(prefix.Groups["size"].Value, out var prefixBytes))
            return new LegacyCatalogLineEntry(prefix.Groups["path"].Value.Trim(), prefixBytes, false);

        return new LegacyCatalogLineEntry(path, null, true);
    }

    public static string Format(string path, long? sizeBytes)
    {
        if (sizeBytes is not >= 0)
            return path;

        var segmentStart = path.LastIndexOf('\\') + 1;
        return path[..segmentStart] + $"[{LegacyScdSize.Format(sizeBytes.Value)}] " + path[segmentStart..];
    }
}

internal readonly record struct LegacyCatalogLineEntry(string Path, long? SizeBytes, bool IsDirectory);
