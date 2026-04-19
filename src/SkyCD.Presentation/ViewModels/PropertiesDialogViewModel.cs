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
        IReadOnlyList<PropertiesInfoItem> infoProperties)
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

    public IReadOnlyList<PropertiesInfoItem> InfoProperties { get; }

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

    private static IReadOnlyList<PropertiesInfoItem> NormalizeInfoProperties(
        IReadOnlyList<PropertiesInfoItem> infoProperties)
    {
        return infoProperties
            .Select(item => new PropertiesInfoItem(
                item.Property,
                NormalizeDisplayValue(item.Value)))
            .OrderBy(item => item.Property, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static string NormalizeDisplayValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GetUnknownText();
        }

        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue ? GetYesText() : GetNoText();
        }

        return value;
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
