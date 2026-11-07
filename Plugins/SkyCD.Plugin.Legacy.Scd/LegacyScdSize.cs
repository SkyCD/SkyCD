using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyCD.Plugin.Legacy.Scd;

internal static class LegacyScdSize
{
    public static bool TryParse(string text, out long bytes)
    {
        bytes = 0;
        var match = Regex.Match(text.Trim(), @"^(?<number>\d+(?:[.,]\d+)?)\s*(?<unit>Bits|Bytes|B|KB|MB|GB|TB)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !decimal.TryParse(match.Groups["number"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        var multiplier = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "BITS" => 0.125m,
            "KB" => 1024m,
            "MB" => 1024m * 1024,
            "GB" => 1024m * 1024 * 1024,
            "TB" => 1024m * 1024 * 1024 * 1024,
            _ => 1m
        };
        // Reject oversized metadata without losing the original path.
        if (value > long.MaxValue / multiplier) return false;
        bytes = (long)decimal.Round(value * multiplier, 0, MidpointRounding.ToEven);
        return true;
    }

    public static string Format(long bytes)
    {
        // Convert2 used bits internally, inclusive unit boundaries, and a ten-character number field.
        if (bytes == 0) return "0          Bits ";
        var (divisor, unit) = bytes switch
        {
            <= 1024 => (1m, " Bytes"),
            <= 1024L * 1024 => (1024m, " KB   "),
            <= 1024L * 1024 * 1024 => (1024m * 1024, " MB   "),
            <= 1024L * 1024 * 1024 * 1024 => (1024m * 1024 * 1024, " GB   "),
            _ => (1024m * 1024 * 1024 * 1024, " TB   ")
        };
        var number = decimal.Round(bytes / divisor, 10).ToString("0.##########", CultureInfo.InvariantCulture);
        return (number.Length > 10 ? number[..10] : number.PadRight(10)) + unit;
    }
}
