using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.MediaCache;

namespace SkyCD.Presentation.ViewModels;

public partial class PropertiesDialogViewModel : ObservableObject
{
    private static readonly HashSet<string> HiddenInfoPropertyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name",
        "RealDirectoryName",
        "Comment"
    };

    private static readonly Regex TimestampPattern = new(
        @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?<offset>Z|[+-][0-9]{2}:[0-9]{2})?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

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

    public System.Collections.ObjectModel.ObservableCollection<TagAssignmentViewModel> TagAssignments { get; } = [];
    public bool HasTagsTab => TagAssignments.Count > 0;

    /// <summary>Optional AI description surface; absent when no description service is available.</summary>
    [ObservableProperty] private AiDescriptionViewModel? aiDescription;

    public bool HasAiDescriptionTab => AiDescription is not null;

    /// <summary>Raised with text the view should place on the clipboard.</summary>
    public event Action<string>? CopyTextRequested;

    partial void OnAiDescriptionChanged(AiDescriptionViewModel? value)
    {
        OnPropertyChanged(nameof(HasAiDescriptionTab));
    }

    /// <summary>
    /// Attaches the offline AI description tab. Reading and clearing stay available without a provider;
    /// adopting writes only the comment editor, so the user still confirms the Properties dialog.
    /// </summary>
    public void InitializeAiDescription(AiDescriptionService service, string entryId)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        if (AiDescription is not null)
        {
            AiDescription.CopyRequested -= OnDescriptionCopyRequested;
        }

        AiDescription = new AiDescriptionViewModel(service, entryId, () => Comments, text => Comments = text);
        AiDescription.CopyRequested += OnDescriptionCopyRequested;
    }

    private void OnDescriptionCopyRequested(string text) => CopyTextRequested?.Invoke(text);

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

    /// <summary>
    /// Lists every reusable tag with the entry's current assignments checked.
    /// Call <see cref="GetAssignedTagIds"/> on confirm; Cancel leaves the
    /// entry untouched because assignment state lives only in this view model.
    /// </summary>
    public void InitializeTags(
        IEnumerable<(string TagId, string Name)> allTags,
        IEnumerable<string> assignedTagIds)
    {
        TagAssignments.Clear();
        var assigned = new HashSet<string>(assignedTagIds ?? [], StringComparer.Ordinal);
        foreach (var (tagId, name) in allTags ?? [])
        {
            if (string.IsNullOrWhiteSpace(tagId))
            {
                continue;
            }

            TagAssignments.Add(new TagAssignmentViewModel(tagId, name ?? tagId, assigned: assigned.Contains(tagId)));
        }
        OnPropertyChanged(nameof(HasTagsTab));
    }

    public IReadOnlyList<string> GetAssignedTagIds() =>
        TagAssignments.Where(item => item.IsAssigned).Select(item => item.TagId).ToArray();

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
        var culture = CultureInfo.CurrentUICulture;
        foreach (var (key, value) in infoProperties)
        {
            if (!HiddenInfoPropertyKeys.Contains(key))
            {
                normalizedDisplay[key] = NormalizeDisplayValue(value, localizer, culture);
            }
        }

        return new PropertiesCollection(normalizedDisplay.ToDictionary(
            item => item.Key,
            item => (object?)item.Value,
            StringComparer.CurrentCultureIgnoreCase));
    }

    private static string NormalizeDisplayValue(
        object? value,
        IStringLocalizer localizer,
        CultureInfo culture)
    {
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

        if (value is DateTimeOffset offsetValue)
        {
            return offsetValue.ToLocalTime().ToString("G", culture);
        }

        if (value is DateTime dateValue)
        {
            return (dateValue.Kind == DateTimeKind.Utc ? dateValue.ToLocalTime() : dateValue).ToString("G", culture);
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

        var timestampMatch = TimestampPattern.Match(text);
        if (timestampMatch.Success)
        {
            if (timestampMatch.Groups["offset"].Success &&
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            {
                return timestamp.ToLocalTime().ToString("G", culture);
            }

            if (!timestampMatch.Groups["offset"].Success &&
                DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var wallClock))
            {
                return wallClock.ToString("G", culture);
            }
        }

        if (string.Equals(text, nameof(CatalogContentsState.NotScanned), StringComparison.OrdinalIgnoreCase))
        {
            var localized = localizer[nameof(CatalogContentsState.NotScanned)].Value;
            if (!string.Equals(localized, nameof(CatalogContentsState.NotScanned), StringComparison.Ordinal))
            {
                return localized;
            }
        }

        if (string.Equals(text, nameof(CatalogContentsState.Incomplete), StringComparison.OrdinalIgnoreCase))
        {
            var localized = localizer[nameof(CatalogContentsState.Incomplete)].Value;
            if (!string.Equals(localized, nameof(CatalogContentsState.Incomplete), StringComparison.Ordinal))
            {
                return localized;
            }
        }

        return text;
    }
}
