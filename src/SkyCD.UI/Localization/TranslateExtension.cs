using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace SkyCD.UI.Localization;

/// <summary>Explicit-source binding independent of a view's compiled data context.</summary>
public sealed class TranslateExtension(string key) : MarkupExtension
{
    public override object ProvideValue(System.IServiceProvider serviceProvider)
    {
        return new Binding($"[{key}]")
        {
            Source = LocalizationService.Current,
            Mode = BindingMode.OneWay
        };
    }
}
