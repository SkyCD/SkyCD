using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public sealed class PropertyValueLocalizer : IStringLocalizer
{
    private readonly CultureInfo? fixedCulture;

    public PropertyValueLocalizer(CultureInfo? fixedCulture = null)
    {
        this.fixedCulture = fixedCulture;
    }

    public LocalizedString this[string name] => new(name, Resolve(name));

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(fixedCulture ?? LocalizationService.Current.CurrentCulture, Resolve(name), arguments));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        yield return this["Unknown"];
        yield return this["Yes"];
        yield return this["No"];
        yield return this["AllSupportedFiles"];
        yield return this["AllFiles"];
    }

    public IStringLocalizer WithCulture(CultureInfo culture)
    {
        return new PropertyValueLocalizer(culture);
    }

    private string Resolve(string key)
    {
        var value = LocalizationService.Current.Get("core.values." + key, fixedCulture ?? CultureInfo.CurrentUICulture);
        return value == "core.values." + key ? key : value;
    }
}
