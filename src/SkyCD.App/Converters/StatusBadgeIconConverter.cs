using System;
using System.Globalization;
using Avalonia.Data.Converters;
using IconPacks.Avalonia;
using SkyCD.App.Icons;

namespace SkyCD.App.Converters;

public sealed class StatusBadgeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string key && StatusIconResolver.TryResolveKind(key, out var iconKind) && iconKind is not null
            ? new PackIconControl { Kind = iconKind }
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
