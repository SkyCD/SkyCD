using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using SkyCD.UI.Localization;

namespace SkyCD.UI.Controls.Preview;

/// <summary>
/// Builds screen-reader names for cached previews and thumbnails. The name is a localized template that
/// includes the entry name, and it re-evaluates when the culture changes.
/// </summary>
public static class PreviewAutomationBinding
{
    /// <summary>Localized name for a browser thumbnail of the bound item.</summary>
    public static MultiBinding CreateThumbnailName() => Create(PreviewKeys.ThumbnailAutomation);

    /// <summary>Localized name for the large preview of the bound entry.</summary>
    public static MultiBinding CreatePreviewImageName() => Create(PreviewKeys.PreviewImageAutomation);

    private static MultiBinding Create(string key) => new()
    {
        Bindings =
        {
            new Binding("Name"),
            new Binding(nameof(LocalizationService.CurrentCulture)) { Source = LocalizationService.Current }
        },
        Converter = new TemplateConverter(key)
    };

    private sealed class TemplateConverter(string key) : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            var name = values.Count > 0 ? values[0] as string : null;
            return LocalizationService.Current.Format(key, name ?? string.Empty);
        }
    }
}
