using System.Globalization;

namespace SkyCD.Presentation.ViewModels;

public static class LanguageCultureResolver
{
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

        try
        {
            return CultureInfo.GetCultureInfo(normalized);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }
}
