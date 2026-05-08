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
        InfoProperties = NormalizeInfoProperties(infoProperties, localizer ?? new PropertyValueLocalizer());
    }

    public string ObjectKey { get; }

    [ObservableProperty]
    private string name;

    public string IconGlyph { get; }

    public PropertiesCollection InfoProperties { get; }

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
