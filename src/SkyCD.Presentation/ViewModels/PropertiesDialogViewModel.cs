using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Abstractions.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class PropertiesDialogViewModel : ObservableObject
{
    private readonly II18nService i18n;

    public PropertiesDialogViewModel(
        string objectKey,
        string name,
        string iconGlyph,
        string comments,
        IReadOnlyDictionary<string, object?> infoProperties)
        : this(objectKey, name, iconGlyph, comments, infoProperties, new I18nService())
    {
    }

    public PropertiesDialogViewModel(
        string objectKey,
        string name,
        string iconGlyph,
        string comments,
        IReadOnlyDictionary<string, object?> infoProperties,
        II18nService i18n)
    {
        this.i18n = i18n ?? throw new ArgumentNullException(nameof(i18n));
        ObjectKey = objectKey;
        this.name = name;
        IconGlyph = iconGlyph;
        this.comments = comments;
        InfoProperties = NormalizeInfoProperties(infoProperties, this.i18n);
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
        IReadOnlyDictionary<string, object?> infoProperties,
        II18nService i18n)
    {
        return infoProperties
            .OrderBy(item => item.Key, StringComparer.CurrentCultureIgnoreCase)
            .ToDictionary(
                item => item.Key,
                item => (object?)NormalizeDisplayValue(item.Value, i18n),
                StringComparer.CurrentCultureIgnoreCase);
    }

    private static string NormalizeDisplayValue(object? value, II18nService i18n)
    {
        if (value is null)
        {
            return i18n.Get("common.unknown");
        }

        if (value is bool boolValue)
        {
            return boolValue ? i18n.Get("common.yes") : i18n.Get("common.no");
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return i18n.Get("common.unknown");
        }

        if (bool.TryParse(text, out var parsedBool))
        {
            return parsedBool ? i18n.Get("common.yes") : i18n.Get("common.no");
        }

        return text;
    }
}
