using System;
using System.Globalization;
using System.IO;
using SkyCD.Plugin.Feature.Split.Exceptions;

namespace SkyCD.Plugin.Feature.Split.Parsing;

/// <summary>
/// Expands the part file naming pattern. <c>#N</c> becomes the legacy hash plus the part number, so the default
/// pattern keeps producing <c>part #1.scd</c>; <c>{n}</c> is the bare part number, <c>{total}</c> the part count
/// and <c>{source}</c> the source catalog name.
/// </summary>
public static class SplitPattern
{
    /// <summary>The legacy SkyCD Split part naming, kept as the default so parts match the old output.</summary>
    public const string Default = "part #N.scd";

    private const string LegacyPartNumberToken = "#N";
    private const string PartNumberToken = "{n}";
    private const string PartCountToken = "{total}";
    private const string SourceNameToken = "{source}";

    /// <summary>Validates a pattern and returns it trimmed, so an unsafe pattern fails before any file is read.</summary>
    public static string Normalize(string? pattern)
    {
        var candidate = (pattern ?? string.Empty).Trim();
        if (candidate.Length == 0)
        {
            throw new SplitPatternException(candidate, "it is empty");
        }

        if (!HasPartNumberToken(candidate))
        {
            throw new SplitPatternException(candidate, "it has no part number token (#N or {n})");
        }

        if (Path.GetExtension(candidate).Length == 0)
        {
            throw new SplitPatternException(candidate, "it has no file extension");
        }

        EnsureSafeFileName(candidate, candidate);
        return candidate;
    }

    /// <summary>Builds one part file name from a normalized pattern.</summary>
    public static string Build(string pattern, int partNumber, int partCount, string sourceName)
    {
        var number = partNumber.ToString(CultureInfo.InvariantCulture);
        var fileName = pattern
            .Replace(LegacyPartNumberToken, "#" + number, StringComparison.OrdinalIgnoreCase)
            .Replace(PartNumberToken, number, StringComparison.OrdinalIgnoreCase)
            .Replace(PartCountToken, partCount.ToString(CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase)
            .Replace(SourceNameToken, sourceName, StringComparison.OrdinalIgnoreCase);
        if (Path.GetFileName(fileName) != fileName)
        {
            throw new SplitPatternException(pattern, "the expanded name is not a plain file name");
        }

        EnsureSafeFileName(fileName, pattern);
        return fileName;
    }

    private static bool HasPartNumberToken(string pattern) =>
        pattern.Contains(LegacyPartNumberToken, StringComparison.OrdinalIgnoreCase)
        || pattern.Contains(PartNumberToken, StringComparison.OrdinalIgnoreCase);

    private static void EnsureSafeFileName(string fileName, string pattern)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var character in fileName)
        {
            if (character is '/' or '\\' or ':'
                || character == Path.DirectorySeparatorChar
                || character == Path.AltDirectorySeparatorChar)
            {
                throw new SplitPatternException(pattern, "it addresses a location outside the output folder");
            }

            if (char.IsControl(character) || Array.IndexOf(invalid, character) >= 0)
            {
                throw new SplitPatternException(pattern, $"it contains the file name character '{character}'");
            }
        }
    }
}
