using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace SkyCD.UI.Localization;

/// <summary>Localizes a document's display type without changing its stored enum identity.</summary>
internal static class DisplayTypeBinding
{
    public static MultiBinding Create()
    {
        return new MultiBinding
        {
            Bindings =
            {
                new Binding("DisplayType"),
                new Binding(nameof(LocalizationService.CurrentCulture)) { Source = LocalizationService.Current }
            },
            Converter = new DisplayTypeConverter()
        };
    }

    private sealed class DisplayTypeConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 0 || values[0] is not string displayType)
            {
                return null;
            }

            var key = ExtraKeys.TypePrefix + displayType.Replace(" ", string.Empty, StringComparison.Ordinal);
            var translated = LocalizationService.Current.Get(key);
            return translated == key ? displayType : translated;
        }
    }
}
