using System;
using System.Globalization;
using Avalonia.Data.Converters;
using IconPacks.Avalonia;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Converters;

public sealed class StatusBadgeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string key && StatusIconCatalog.TryResolveKind(key, out var kind) && kind is Enum iconKind
            ? new PackIconControl { Kind = iconKind }
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
