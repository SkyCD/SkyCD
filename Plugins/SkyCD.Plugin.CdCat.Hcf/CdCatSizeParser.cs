using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyCD.Plugin.CdCat.Hcf;

/// <summary>
/// Parses CdCat size representations into 64-bit integer byte counts.
/// </summary>
public static partial class CdCatSizeParser
{
    private static readonly Regex SizeRegex = SizePattern();

    public static long ParseSizeBytes(string? sizeString)
    {
        if (string.IsNullOrWhiteSpace(sizeString))
        {
            return 0L;
        }

        var trimmed = sizeString.Trim();

        // Direct integer parse attempt
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var directBytes))
        {
            return directBytes >= 0 ? directBytes : 0L;
        }

        var match = SizeRegex.Match(trimmed);
        if (!match.Success)
        {
            return 0L;
        }

        var numberPart = match.Groups["num"].Value.Replace(',', '.');
        if (!double.TryParse(numberPart, NumberStyles.Float | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var num))
        {
            return 0L;
        }

        if (num < 0)
        {
            return 0L;
        }

        var unitPart = match.Groups["unit"].Value.Trim().ToLowerInvariant();

        var multiplier = unitPart switch
        {
            "byte" or "bytes" or "b" => 1.0,
            "kb" or "kib" or "k" => 1024.0,
            "mb" or "mib" or "m" => 1024.0 * 1024.0,
            "gb" or "gib" or "g" => 1024.0 * 1024.0 * 1024.0,
            "tb" or "tib" or "t" => 1024.0 * 1024.0 * 1024.0 * 1024.0,
            _ => 1.0
        };

        var calculated = num * multiplier;
        if (calculated >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)Math.Round(calculated, MidpointRounding.AwayFromZero);
    }

    [GeneratedRegex(@"^(?<num>[\d\.\,]+)\s*(?<unit>[a-zA-Z]*)$", RegexOptions.Compiled)]
    private static partial Regex SizePattern();
}
