using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace SkyCD.App.Converters;

/// <summary>
/// Decodes the full-color frame extracted from a plugin assembly's ICO.
/// </summary>
public sealed class PluginBitmapConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } icon)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(icon);
            return new Bitmap(stream);
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
