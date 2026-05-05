using System;

namespace SkyCD.Presentation.ViewModels;

public static class AboutDialogFormatting
{
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kiloBytes = bytes / 1024d;
        if (kiloBytes < 1024d)
        {
            return $"{kiloBytes:0.0} KB";
        }

        var megaBytes = kiloBytes / 1024d;
        if (megaBytes < 1024d)
        {
            return $"{megaBytes:0.0} MB";
        }

        var gigaBytes = megaBytes / 1024d;
        return $"{gigaBytes:0.0} GB";
    }

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
