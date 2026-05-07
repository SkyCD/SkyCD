using System;
namespace SkyCD.Presentation.ViewModels;

public static class AboutDialogFormatting
{
    public static string FormatFriendlyTime(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours:00}h {duration.Minutes:00}m";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{duration.Hours:00}h {duration.Minutes:00}m {duration.Seconds:00}s";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes:00}m {duration.Seconds:00}s";
        }

        return $"{duration.Seconds:00}s";
    }

    public static string FormatStartTime(DateTime startTime)
    {
        return startTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
