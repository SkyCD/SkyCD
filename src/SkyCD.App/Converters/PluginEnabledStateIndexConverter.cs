using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SkyCD.App.Converters;

/// <summary>
/// Maps a plugin enabled flag to a two-entry selector index (0 = enabled, 1 = disabled) so a ComboBox
/// bound to <c>OptionsPluginItem.IsEnabled</c> shows the matching state label.
/// </summary>
public sealed class PluginEnabledStateIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool enabled ? (enabled ? 0 : 1) : 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index ? index == 0 : true;
}
