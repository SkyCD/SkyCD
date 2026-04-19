using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SkyCD.Presentation.ViewModels;

public partial class PropertiesDialogViewModel : ObservableObject
{
    public PropertiesDialogViewModel(
        string objectKey,
        string name,
        string iconGlyph,
        string comments,
        IReadOnlyDictionary<string, object?> infoProperties)
    {
        ObjectKey = objectKey;
        this.name = name;
        IconGlyph = iconGlyph;
        this.comments = comments;
        InfoProperties = NormalizeInfoProperties(infoProperties);
    }

    public string ObjectKey { get; }

    [ObservableProperty]
    private string name;

    public string IconGlyph { get; }

    public IReadOnlyDictionary<string, object?> InfoProperties { get; }

    public bool HasInfoTab => InfoProperties.Count > 0;

    [ObservableProperty]
    private string comments;

    [ObservableProperty]
    private bool dialogAccepted;

    [RelayCommand]
    private void Confirm()
    {
        DialogAccepted = true;
    }

    private static IReadOnlyDictionary<string, object?> NormalizeInfoProperties(
        IReadOnlyDictionary<string, object?> infoProperties)
    {
        return infoProperties
            .OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(
                item => item.Key,
                item => (object?)NormalizeDisplayValue(item.Value),
                StringComparer.CurrentCultureIgnoreCase);
    }

    private static string NormalizeDisplayValue(object? value)
    {
        if (value is null)
        {
            return GetUnknownText();
        }

        if (value is bool boolValue)
        {
            return boolValue ? GetYesText() : GetNoText();
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return GetUnknownText();
        }

        if (bool.TryParse(text, out var parsedBool))
        {
            return parsedBool ? GetYesText() : GetNoText();
        }

        return text;
    }

    private static string GetUnknownText()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("lt", StringComparison.OrdinalIgnoreCase)
            ? "Nežinoma"
            : "Unknown";
    }

    private static string GetYesText()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("lt", StringComparison.OrdinalIgnoreCase)
            ? "Taip"
            : "Yes";
    }

    private static string GetNoText()
    {
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("lt", StringComparison.OrdinalIgnoreCase)
            ? "Ne"
            : "No";
    }
}
