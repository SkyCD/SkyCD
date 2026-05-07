using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyCD.Presentation.ViewModels;

public static class LanguageCultureResolver
{
    private static readonly Regex CultureCodePattern = new(
        "^[a-zA-Z]{2,3}(?:-[a-zA-Z0-9]{2,8})*$",
        RegexOptions.Compiled);

    public static CultureInfo ResolveCulture(string? languageName)
    {
        if (string.IsNullOrWhiteSpace(languageName))
        {
            return CultureInfo.GetCultureInfo("en-US");
        }

        var normalized = languageName.Trim();
        if (normalized.Equals("English", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("en-US");
        }

        if (normalized.Equals("Lithuanian", StringComparison.OrdinalIgnoreCase))
        {
            return CultureInfo.GetCultureInfo("lt-LT");
        }

        if (!LooksLikeCultureCode(normalized))
        {
            return CultureInfo.GetCultureInfo("en-US");
        }

        try
        {
            return CultureInfo.GetCultureInfo(normalized);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }

    private static bool LooksLikeCultureCode(string value)
    {
        return CultureCodePattern.IsMatch(value);
    }
}
