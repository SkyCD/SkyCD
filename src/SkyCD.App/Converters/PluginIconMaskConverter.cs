using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SkyCD.App.Converters;

/// <summary>
/// Turns the PNG icon embedded in a plugin assembly into an opacity mask, so the Options page paints the
/// plugin's own artwork with the same theme foreground it uses for icon-pack glyphs.
/// </summary>
public sealed class PluginIconMaskConverter : IValueConverter
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
            return new ImageBrush(new Bitmap(stream)) { Stretch = Stretch.Uniform };
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
