using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SkyCD.App.Icons;

namespace SkyCD.App.Converters;

public sealed class IconGlyphToKindConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string key && StatusIconResolver.TryResolveKind(key, out var kind)
            ? kind
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
