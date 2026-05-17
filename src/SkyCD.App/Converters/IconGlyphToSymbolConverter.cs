using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SkyCD.App.Converters;

public sealed class IconGlyphToSymbolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value?.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return "•";
        }

        var normalized = key.ToLowerInvariant();
        if (normalized.Contains(':'))
        {
            normalized = normalized[(normalized.IndexOf(':') + 1)..];
        }

        return normalized switch
        {
            "check" => "✓",
            "checkcheck" => "✓✓",
            "clock" or "clock3" => "◷",
            "star" => "★",
            "listtodo" => "☰",
            "pause" or "pausecircle" => "⏸",
            "xcircle" => "⊗",
            "calendarplus" => "⊞",
            "rotateccw" => "↺",
            "packagecheck" => "☑",
            "handshake" => "⇄",
            "search" => "⌕",
            "warning" or "trianglealert" => "⚠",
            "archive" => "⌂",
            _ => GetFallbackSymbol(normalized)
        };
    }

    private static string GetFallbackSymbol(string key)
    {
        var symbols = new[]
        {
            "●", "◆", "■", "▲", "◉", "◇", "□", "△", "✦", "✧", "✪", "✶"
        };

        var index = Math.Abs(key.GetHashCode()) % symbols.Length;
        return symbols[index];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
