using System;
using System.Globalization;
using Avalonia.Data.Converters;
using SkyCD.Presentation.Search;
using SkyCD.UI.Localization;

namespace SkyCD.App.Converters;

public sealed class AdvancedCatalogFieldConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        AdvancedCatalogField.Location => LocalizationService.Current.Get(UiKeys.Location),
        AdvancedCatalogField.Contributed => LocalizationService.Current.Get(TextKeys.AdvancedFilterPluginField),
        _ => value?.ToString()
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
