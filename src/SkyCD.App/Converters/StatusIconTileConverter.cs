using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using IconPacks.Avalonia;
using SkyCD.App.Icons;

namespace SkyCD.App.Converters;

/// <summary>
/// Renders one icon of the picker's tile sheet in its pack color. The color is derived from the
/// pack name only, so every icon of a pack shares one tint, tabs and sheet stay visually linked,
/// and a stored key always resolves to the same color (issue #1214).
/// </summary>
public sealed class StatusIconTileConverter : IValueConverter
{
    // 400-grade tints stay legible on the dark shell while keeping the sheet colorful.
    private static readonly IBrush[] PackPalette =
    [
        CreateBrush("#F87171"), CreateBrush("#FB923C"), CreateBrush("#FBBF24"), CreateBrush("#34D399"),
        CreateBrush("#22D3EE"), CreateBrush("#60A5FA"), CreateBrush("#A78BFA"), CreateBrush("#F472B6"),
        CreateBrush("#84CC16"), CreateBrush("#2DD4BF"), CreateBrush("#E879F9"), CreateBrush("#FCD34D")
    ];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string key || !StatusIconResolver.TryResolveKind(key, out var iconKind) || iconKind is null)
        {
            return null;
        }

        return new PackIconControl
        {
            Kind = iconKind,
            Foreground = PackPalette[GetPackColorIndex(key)]
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static int GetPackColorIndex(string key)
    {
        var separator = key.IndexOf(':');
        var pack = separator > 0 ? key[..separator] : key;
        var hash = 2166136261u;
        foreach (var character in pack.ToUpperInvariant())
        {
            hash ^= character;
            hash *= 16777619u;
        }

        return (int)(hash % (uint)PackPalette.Length);
    }

    private static IBrush CreateBrush(string color) => new SolidColorBrush(Color.Parse(color));
}
