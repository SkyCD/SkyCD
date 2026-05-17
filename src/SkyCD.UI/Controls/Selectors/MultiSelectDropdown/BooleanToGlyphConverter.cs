using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SkyCD.UI.Controls.Selectors.MultiSelectDropdown;

public sealed class BooleanToGlyphConverter : IValueConverter
{
    public static BooleanToGlyphConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "✓" : " ";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return false;
    }
}
