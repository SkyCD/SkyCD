using System;
using System.Globalization;

namespace SkyCD.Plugin.DiskExplorer.Ded;

/// <summary>
/// Helpers for raw Windows FILETIME values. Raw values are always preserved;
/// derived display text is produced only when the value fits a UTC
/// <see cref="DateTime"/> without loss.
/// </summary>
public static class DedFileTime
{
    /// <summary>Returns the UTC date text (yyyy-MM-dd), or null when out of range.</summary>
    public static string? ToDateText(ulong fileTime)
    {
        if (!TryToUtc(fileTime, out var value))
        {
            return null;
        }

        return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Returns the UTC time text (HH:mm:ss), or null when out of range.</summary>
    public static string? ToTimeText(ulong fileTime)
    {
        if (!TryToUtc(fileTime, out var value))
        {
            return null;
        }

        return value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Converts a raw FILETIME to UTC when the value is representable.</summary>
    public static bool TryToUtc(ulong fileTime, out DateTime value)
    {
        if (fileTime > (ulong)DateTime.MaxValue.ToFileTimeUtc())
        {
            value = default;
            return false;
        }

        value = DateTime.FromFileTimeUtc((long)fileTime);
        return true;
    }

    /// <summary>Converts a UTC <see cref="DateTime"/> to a raw FILETIME value.</summary>
    public static ulong FromUtc(DateTime value) => (ulong)value.ToFileTimeUtc();
}
