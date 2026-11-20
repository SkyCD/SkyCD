using System;
using System.Globalization;

namespace SkyCD.Documents;

/// <summary>
/// Reads and writes the user rating stored in <c>Properties["Rating"]</c>. The rating is a whole-star value
/// from 1 to <see cref="MaximumValue"/>; <see cref="Unrated"/> (0) removes the property so unrated entries
/// carry no stored value at all.
/// </summary>
/// <remarks>
/// The key is classified as a user annotation by
/// <c>SkyCD.Presentation.MediaCache.MediaAnnotationPolicy</c>, so rescanning never overwrites it.
/// </remarks>
public static class StarRating
{
    /// <summary>Catalog property key that stores the rating.</summary>
    public const string PropertyKey = "Rating";

    /// <summary>Value that represents "not rated"; it is never persisted.</summary>
    public const int Unrated = 0;

    /// <summary>Highest selectable rating.</summary>
    public const int MaximumValue = 5;

    /// <summary>
    /// Parses a stored rating. Missing, empty, non-numeric and out-of-range values read as
    /// <see cref="Unrated"/> so foreign imports cannot make the editor throw.
    /// </summary>
    public static int Parse(object? storedValue)
    {
        if (storedValue is null || TryParseNumber(storedValue, out var number) is false)
        {
            return Unrated;
        }

        return ToStars(number);
    }

    /// <summary>Converts a numeric rating to stars, rounding halves up, clamping to 0-5 and never returning 0 for a positive value.</summary>
    public static int ToStars(double number)
    {
        if (double.IsNaN(number) || number <= 0)
        {
            return Unrated;
        }

        return (int)Math.Clamp(Math.Round(number, MidpointRounding.AwayFromZero), 1, MaximumValue);
    }

    /// <summary>Value written to properties for <paramref name="stars"/>: the invariant star count, or null when unrated.</summary>
    public static string? ToStoredValue(int stars) =>
        stars <= Unrated ? null : Math.Clamp(stars, 1, MaximumValue).ToString(CultureInfo.InvariantCulture);

    private static bool TryParseNumber(object value, out double number)
    {
        number = 0;
        switch (value)
        {
            case int intValue:
                number = intValue;
                return true;
            case long longValue:
                number = longValue;
                return true;
            case short or byte or sbyte or ushort or uint or ulong:
                number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            case double doubleValue:
                number = doubleValue;
                return !double.IsNaN(doubleValue) && !double.IsInfinity(doubleValue);
            case float floatValue:
                number = floatValue;
                return !float.IsNaN(floatValue) && !float.IsInfinity(floatValue);
            case decimal decimalValue:
                number = (double)decimalValue;
                return true;
        }

        var text = value.ToString()?.Trim();
        return !string.IsNullOrEmpty(text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && !double.IsNaN(number)
            && !double.IsInfinity(number);
    }
}
