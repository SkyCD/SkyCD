using System;
using System.Globalization;
using System.Text;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Shared text handling for metadata parts: folding whitespace and reading W3C date-time values.
/// </summary>
internal static class OfficeText
{
    /// <summary>Folds every whitespace run to one space and trims; a blank result becomes <see langword="null"/>.</summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (character is '\0' || char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Parses a W3C date-time value and normalizes it to UTC; a value without a zone is taken as UTC.</summary>
    public static DateTimeOffset? ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
        {
            return null;
        }

        return value.Year is < 1601 or > 9999 ? null : value;
    }

    /// <summary>Parses a positive decimal statistic; a missing, malformed or zero value yields <see langword="null"/>.</summary>
    public static long? ParsePositiveInteger(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || !long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            || value <= 0)
        {
            return null;
        }

        return value;
    }
}
