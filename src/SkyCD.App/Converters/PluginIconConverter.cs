using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SkyCD.App.Converters;

public sealed class PluginIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } iconKey)
        {
            return null;
        }

        try
        {
            return IconGlyphConverter.Resolve(iconKey);
        }
        catch
        {
            // A plugin-specific icon must never break the plugin list.
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
