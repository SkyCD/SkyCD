using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace SkyCD.App.Converters;

public sealed class MenuHotKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string gestureText)
        {
            if (string.IsNullOrWhiteSpace(gestureText))
            {
                return null;
            }

            try
            {
                return KeyGesture.Parse(gestureText);
            }
            catch
            {
                return null;
            }
        }

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
