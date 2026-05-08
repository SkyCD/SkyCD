using System;
using System.Globalization;
using Humanizer;

namespace SkyCD.Formatting;

public static class TimeFormatting
{
    public static string FormatAboutDialogDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var precision = duration.TotalDays >= 1 ? 3
            : duration.TotalHours >= 1 ? 3
            : duration.TotalMinutes >= 1 ? 2
            : 1;
        var minUnit = duration.TotalDays >= 1
            ? TimeUnit.Minute
            : TimeUnit.Second;

        return duration.Humanize(
            precision: precision,
            minUnit: minUnit,
            maxUnit: TimeUnit.Day,
            collectionSeparator: " ",
            culture: CultureInfo.GetCultureInfo("en-US"));
    }
}
