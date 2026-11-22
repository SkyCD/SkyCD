using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Localization;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Formatting;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Presentation.MediaCache;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class PropertiesDialogViewModel : ObservableObject, IDisposable
{
    private const string DescriptionPropertyKey = "Description";

    private static readonly HashSet<string> HiddenInfoPropertyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name",
        "RealDirectoryName",
        "Comment",
        // The description is shown once as a unified value with its author annotation.
        DescriptionPropertyKey,
        "LocationId",
        // The rating is edited with the star control at the top of the General tab.
        StarRating.PropertyKey
    };

    private static readonly Regex TimestampPattern = new(
        @"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?<offset>Z|[+-][0-9]{2}:[0-9]{2})?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private readonly PropertiesCollection sourceProperties;
    private readonly IStringLocalizer valueLocalizer;
    private readonly string? catalogDescription;

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
        catalogDescription = ReadCatalogDescription(sourceProperties);
        valueLocalizer = localizer ?? new PropertyValueLocalizer();
        InfoProperties = NormalizeInfoProperties(sourceProperties, valueLocalizer);
        rating = ReadStoredRating();
        if (catalogDescription is not null)
        {
            // Imported or user-entered descriptions render without AI actions even when no provider is available.
            Description = new DescriptionViewModel(catalogDescription, null, objectKey);
        }

        LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
    }

    public System.Collections.ObjectModel.ObservableCollection<CustomPropertyItemViewModel> CustomProperties { get; } = [];
    public bool HasCustomProperties => CustomProperties.Count > 0;

    public System.Collections.ObjectModel.ObservableCollection<TagAssignmentViewModel> TagAssignments { get; } = [];
    public bool HasTagsTab => TagAssignments.Count > 0;

    /// <summary>Unified description surface; absent only when the entry has no description to show.</summary>
    [ObservableProperty] private DescriptionViewModel? description;

    /// <summary>Tabs contributed by enabled feature plugins; empty when no plugin offers a surface.</summary>
    public System.Collections.ObjectModel.ObservableCollection<PropertiesDetailsTabViewModel> DetailsTabs { get; } = [];
    public bool HasDetailsTabs => DetailsTabs.Count > 0;

    /// <summary>Key of the contributed tab the dialog should select on open, when one was requested.</summary>
    public string? InitialDetailsTabKey { get; set; }

    /// <summary>
    /// Attaches the tabs contributed by enabled plugins for the selected entry, replacing any previously
    /// attached tabs. A failing contribution is isolated so the rest of the dialog still opens.
    /// </summary>
    public void InitializeDetailsTabs(IEnumerable<EntryDetailsTabContribution>? contributions)
    {
        DisposeDetailsTabs();
        foreach (var contribution in contributions ?? [])
        {
            try
            {
                DetailsTabs.Add(new PropertiesDetailsTabViewModel(
                    contribution.Key,
                    contribution.Title,
                    contribution.CreateTab()));
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(typeof(EntryDetailsTabContribution), "catalog", exception);
            }
        }

        OnPropertyChanged(nameof(HasDetailsTabs));
    }

    /// <summary>Releases the plugin-owned tab content.</summary>
    public void Dispose() => DisposeDetailsTabs();

    private void DisposeDetailsTabs()
    {
        foreach (var tab in DetailsTabs)
        {
            tab.Dispose();
        }

        DetailsTabs.Clear();
    }

    public bool HasDescriptionSection => Description is not null;

    /// <summary>Raised with text the view should place on the clipboard.</summary>
    public event Action<string>? CopyTextRequested;

    partial void OnDescriptionChanged(DescriptionViewModel? value)
    {
        OnPropertyChanged(nameof(HasDescriptionSection));
    }

    /// <summary>
    /// Attaches the offline AI description actions to the unified description field. Imported or
    /// user-entered catalog text keeps winning; AI output only fills an empty description.
    /// </summary>
    public void InitializeDescription(AiDescriptionService service, string entryId)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        if (Description is not null)
        {
            Description.CopyRequested -= OnDescriptionCopyRequested;
        }

        Description = new DescriptionViewModel(catalogDescription, service, entryId);
        Description.CopyRequested += OnDescriptionCopyRequested;
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
        ApplyRating(result);
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
        OnPropertyChanged(nameof(RatingValueText));
    }

    public bool HasInfoTab => InfoProperties.Count > 0;

    /// <summary>
    /// Draft rating shown by the star control. Picking or clearing stars changes only this view model; the
    /// catalog entry receives the value through <see cref="GetProperties"/> when the dialog is confirmed, and
    /// Cancel simply drops the view model so the stored rating is restored the next time properties open.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RatingValueText))]
    private int rating;

    /// <summary>Number of stars currently selected, 0 when the entry is unrated.</summary>
    public string RatingValueText =>
        Rating <= StarRating.Unrated
            ? LocalizationService.Current.Get(UiKeys.RatingNotSet)
            : LocalizationService.Current.Format(UiKeys.RatingValue, Rating, StarRating.MaximumValue);

    private int ReadStoredRating() =>
        sourceProperties.TryGetValue(StarRating.PropertyKey, out var stored) ? StarRating.Parse(stored) : StarRating.Unrated;

    private static string? ReadCatalogDescription(PropertiesCollection properties)
    {
        if (!properties.TryGetValue(DescriptionPropertyKey, out var value) || value is null)
        {
            return null;
        }

        var text = Convert.ToString(value, CultureInfo.CurrentCulture);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private void ApplyRating(PropertiesCollection result)
    {
        // Compare against the stored value so an unchanged draft leaves the property byte-for-byte intact.
        var storedRating = result.TryGetValue(StarRating.PropertyKey, out var storedValue) ? StarRating.Parse(storedValue) : StarRating.Unrated;
        if (Rating == storedRating)
        {
            return;
        }

        if (StarRating.ToStoredValue(Rating) is { } stars)
        {
            result[StarRating.PropertyKey] = stars;
        }
        else
        {
            result.Remove(StarRating.PropertyKey);
        }
    }

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
                // Display keys localize through the metadata catalog and fall back to the stored key.
                normalizedDisplay[MediaMetadataValueFormatter.ResolveLabel(key)] =
                    NormalizeDisplayValue(key, value, localizer, culture);
            }
        }

        return new PropertiesCollection(normalizedDisplay.ToDictionary(
            item => item.Key,
            item => (object?)item.Value,
            StringComparer.CurrentCultureIgnoreCase));
    }

    private static string NormalizeDisplayValue(
        string key,
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

        if ((key.EndsWith("Size", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(key, "sizeBytes", StringComparison.OrdinalIgnoreCase)) &&
            value is string or byte or sbyte or short or ushort or int or uint or long or ulong &&
            Convert.ToString(value, CultureInfo.InvariantCulture) is { } rawValue &&
            rawValue.All(char.IsAsciiDigit) &&
            ulong.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes))
        {
            return $"{SizeFormatting.FormatBytes(bytes, "0.##")} ({bytes.ToString("N0", culture)} bytes)";
        }

        if (string.Equals(key, "Attributes", StringComparison.OrdinalIgnoreCase) &&
            FileAttributesFormatting.TryGetMask(value, out var attributeMask))
        {
            return FileAttributesFormatting.FormatFileAttributes(
                attributeMask,
                localizationKey =>
                {
                    var localized = localizer[localizationKey].Value;
                    return string.Equals(localized, localizationKey, StringComparison.Ordinal) ? null : localized;
                });
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
