using Avalonia.Markup.Xaml.Templates;
using System;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;
using System.Globalization;
using SkyCD.Services;

namespace SkyCD.MarkupExtensions
{
    public class LocExtension : MarkupExtension
    {
        public string Key { get; set; } = string.Empty;

        public override object? ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key))
                return string.Empty;

            // trim possible quotes that may appear in XAML
            var key = Key.Trim().Trim('"');
            var value = LocalizationService.Instance.T(key);
            System.Diagnostics.Debug.WriteLine($"LocExtension: key='{key}' -> '{value}'");
            return value;
        }
    }
}
