using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using IconPacks.Avalonia;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Converters;

public sealed class MenuIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is StatusMenuIcon statusIcon &&
            StatusIconCatalog.TryResolveKind(statusIcon.IconGlyph, out var kind) &&
            kind is not null)
        {
            return new PackIconControl
            {
                Kind = (Enum)kind,
                Width = 14,
                Height = 14,
                Foreground = StatusIconCatalog.ResolveBrush(statusIcon.IconColor)
            };
        }

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
