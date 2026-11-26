using System;
using System.Globalization;
using Avalonia.Data.Converters;
using IconPacks.Avalonia;
using SkyCD.App.Icons;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Converters;

public sealed class MenuIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not StatusMenuIcon statusIcon)
        {
            return value;
        }

        return StatusIconResolver.TryResolveKind(statusIcon.IconGlyph, out var iconKind) && iconKind is not null
            ? new PackIconControl
            {
                Kind = iconKind,
                Width = 14,
                Height = 14,
                Foreground = StatusIconResolver.ResolveBrush(statusIcon.IconColor)
            }
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
