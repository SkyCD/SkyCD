using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SkyCD.Plugin.EBook.Parsing;

/// <summary>Normalization shared by every format reader, so a value looks the same whichever book carried it.</summary>
internal static class EBookText
{
    /// <summary>Folds whitespace and NUL characters into single spaces and trims; blank text becomes null.</summary>
    public static string? Normalize(string? text, int maxCharacters = int.MaxValue)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(text.Length, maxCharacters));
        var pendingSpace = false;
        foreach (var character in text)
        {
            if (builder.Length == maxCharacters)
            {
                break;
            }

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

    /// <summary>
    /// Parses the date forms e-books carry: ISO instants, calendar dates and the textual dates some
    /// Mobipocket files use. A date without a time is taken as midnight UTC; a partial date such as a bare
    /// year is dropped rather than completed with an invented month or day.
    /// </summary>
    public static DateTimeOffset? ParseTimestamp(string? text)
    {
        var value = Normalize(text);
        if (value is null || value.Length > 64)
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
        {
            return date;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant))
        {
            return instant.Year is >= 1601 and <= 9999 ? instant : null;
        }

        return null;
    }

    /// <summary>Reads a bare publication year, the only date form FictionBook documents usually carry.</summary>
    public static int? ParseYear(string? text)
    {
        var value = Normalize(text);
        return value is { Length: 4 } &&
               int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
               year is > 0 and <= 9999
            ? year
            : null;
    }

    /// <summary>Joins names for a tag field so the host can split them back into one tag per author.</summary>
    public static string? JoinValues(IReadOnlyList<string> values) =>
        values.Count == 0 ? null : string.Join("; ", values);
}
