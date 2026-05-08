using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Localization;

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
        new(name, string.Format(Resolve(name), arguments));

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        yield return this["Unknown"];
        yield return this["Yes"];
        yield return this["No"];
    }

    public IStringLocalizer WithCulture(CultureInfo culture)
    {
        return new PropertyValueLocalizer(culture);
    }

    private string Resolve(string key)
    {
        var culture = fixedCulture ?? CultureInfo.CurrentUICulture;
        var isLithuanian = culture.TwoLetterISOLanguageName.Equals("lt", StringComparison.OrdinalIgnoreCase);
        return key switch
        {
            "Unknown" => isLithuanian ? "Nežinoma" : "Unknown",
            "Yes" => isLithuanian ? "Taip" : "Yes",
            "No" => isLithuanian ? "Ne" : "No",
            _ => key
        };
    }
}
