using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SkyCD.Documents.Collections;

namespace SkyCD.Presentation.ViewModels;

public partial class PropertiesDialogViewModel : ObservableObject
{
    private readonly PropertiesCollection sourceProperties;
    private readonly IStringLocalizer valueLocalizer;
    public PropertiesDialogViewModel(
        string objectKey,
        string name,
        string iconGlyph,
        string comments,
        PropertiesCollection infoProperties,
        IStringLocalizer? localizer = null)
    {
        ObjectKey = objectKey;
        this.name = name;
        IconGlyph = iconGlyph;
        this.comments = comments;
        sourceProperties = new PropertiesCollection(infoProperties.ToDictionary(item => item.Key, item => item.Value));
        valueLocalizer = localizer ?? new PropertyValueLocalizer();
        InfoProperties = NormalizeInfoProperties(sourceProperties, valueLocalizer);
        LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
    }

    public System.Collections.ObjectModel.ObservableCollection<CustomPropertyItemViewModel> CustomProperties { get; } = [];
    public bool HasCustomProperties => CustomProperties.Count > 0;

    public void ApplyCustomPropertyDefaults(IEnumerable<SkyCD.Documents.CustomPropertyDocument>? properties)
    {
        CustomProperties.Clear();
        foreach (var property in properties ?? [])
        {
            if (string.IsNullOrWhiteSpace(property.Name))
            {
                continue;
            }
            var key = sourceProperties.Keys.FirstOrDefault(key => string.Equals(key, property.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            var name = key ?? property.Name.Trim();
            if (CustomProperties.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            CustomProperties.Add(new CustomPropertyItemViewModel
            {
                Name = name,
                DefaultValue = key is null ? property.DefaultValue : Convert.ToString(sourceProperties[key], CultureInfo.CurrentCulture) ?? string.Empty
            });
        }
        OnPropertyChanged(nameof(HasCustomProperties));
    }

    public PropertiesCollection GetProperties()
    {
        var result = new PropertiesCollection(sourceProperties);
        foreach (var property in CustomProperties)
        {
            if (!sourceProperties.TryGetValue(property.Name, out var original) ||
                property.DefaultValue != (Convert.ToString(original, CultureInfo.CurrentCulture) ?? string.Empty))
            {
                result[property.Name] = property.DefaultValue;
            }
        }
        return result;
    }

    public string ObjectKey { get; }

    [ObservableProperty] private string name;

    public string IconGlyph { get; }

    public PropertiesCollection InfoProperties { get; private set; }

    private void RefreshLocalization()
    {
        InfoProperties = NormalizeInfoProperties(sourceProperties, valueLocalizer);
        OnPropertyChanged(nameof(InfoProperties));
        OnPropertyChanged(nameof(HasInfoTab));
    }

    public bool HasInfoTab => InfoProperties.Count > 0;

    [ObservableProperty] private string comments;

    [ObservableProperty] private bool dialogAccepted;

    [RelayCommand]
    private void Confirm()
    {
        DialogAccepted = true;
    }

    private static PropertiesCollection NormalizeInfoProperties(
        PropertiesCollection infoProperties,
        IStringLocalizer localizer)
    {
        var normalizedDisplay = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var (key, value) in infoProperties)
        {
            normalizedDisplay[key] = NormalizeDisplayValue(value, localizer);
        }

        return new PropertiesCollection(normalizedDisplay.ToDictionary(
            item => item.Key,
            item => (object?)item.Value,
            StringComparer.CurrentCultureIgnoreCase));
    }

    private static string NormalizeDisplayValue(
        object? value,
        IStringLocalizer localizer,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentUICulture;
        var unknownText = localizer["Unknown"].Value;
        var yesText = localizer["Yes"].Value;
        var noText = localizer["No"].Value;

        if (value is null)
        {
            return unknownText;
        }

        if (value is bool boolValue)
        {
            return boolValue ? yesText : noText;
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return unknownText;
        }

        if (bool.TryParse(text, out var parsedBool))
        {
            return parsedBool ? yesText : noText;
        }

        return text;
    }
}
