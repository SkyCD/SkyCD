using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SkyCD.App.Icons;

namespace SkyCD.App.Converters;

/// <summary>
/// Shows the icon of a toolbar command in the Options toolbar lists (issue #933). The same resolver the
/// toolbar buttons use answers the key, so a list row and its button never disagree (issue #1022). A null or
/// empty icon reference — a separator — yields no image, so the separator row keeps its plain line.
/// </summary>
public sealed class ToolbarIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => ToolbarIconResolver.Resolve(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
