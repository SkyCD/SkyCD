using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.UI.Controls.Selectors.MultiSelectDropdown;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class StatusVariantItemViewModel : ObservableObject
{
    private readonly Dictionary<MultiSelectOptionItem, CatalogDocumentType> optionTypes = [];
    public static IReadOnlyList<CatalogDocumentType> AvailableItemTypes { get; } =
        Enum.GetValues<CatalogDocumentType>();

    [ObservableProperty] private string name = string.Empty;

    [ObservableProperty] private string iconGlyph = string.Empty;
    [ObservableProperty] private string iconColor = "#FFFFFF";

    [ObservableProperty] private bool isDropHintVisible;

    public ObservableCollection<MultiSelectOptionItem> TypeOptions { get; } = [];
    /// <summary>Pack/key strings registered at host startup; resolution happens in App converters.</summary>
    public IReadOnlyList<string> IconOptions => StatusIconCatalog.GetAllKeys();
    public IReadOnlyList<string> ColorOptions { get; } =
    [
        "#000000", "#44546A", "#5B9BD5", "#70AD47", "#A5A5A5", "#FFC000", "#4472C4", "#ED7D31",
        "#1F1F1F", "#2F3A4D", "#2E75B5", "#548235", "#7F7F7F", "#BF9000", "#2F5597", "#C55A11",
        "#595959", "#8497B0", "#9DC3E6", "#A9D18E", "#C9C9C9", "#FFE699", "#8FAADC", "#F4B183",
        "#D9E1F2", "#E2EFDA", "#FCE4D6", "#FFF2CC", "#EDEDED", "#D6E3F3", "#E2F0D9", "#FBE5D6"
    ];

    // IconKind and IconBrush intentionally live in App converters (StatusIconResolver):
    // view models expose the IconGlyph/IconColor strings only, never Avalonia types.

    public StatusVariantItemViewModel()
    {
        foreach (var availableType in AvailableItemTypes)
        {
            var option = new MultiSelectOptionItem
            {
                Label = GetTypeLabel(availableType)
            };
            optionTypes.Add(option, availableType);
            option.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MultiSelectOptionItem.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedItemTypesDisplayText));
                }
            };
            TypeOptions.Add(option);
        }

        LocalizationObserver.Observe(this, static vm => vm.RefreshLocalization());
    }

    public string SelectedItemTypesDisplayText => GetSelectedTypes().Count == 0
        ? string.Empty
        : string.Join(", ", GetSelectedTypes().OrderBy(static value => value).Select(GetTypeLabel));

    public bool IsTypeSelected(CatalogDocumentType type) => GetSelectedTypes().Contains(type);

    public void SetTypeSelected(CatalogDocumentType type, bool isSelected)
    {
        var option = TypeOptions.FirstOrDefault(candidate => optionTypes.TryGetValue(candidate, out var value) && value == type);
        if (option is null)
        {
            return;
        }

        if (option.IsSelected != isSelected)
        {
            option.IsSelected = isSelected;
        }

        OnPropertyChanged(nameof(SelectedItemTypesDisplayText));
    }

    public StatusVariantDocument ToDocument()
    {
        return new StatusVariantDocument
        {
            Name = Name.Trim(),
            IconGlyph = IconGlyph.Trim(),
            IconColor = string.IsNullOrWhiteSpace(IconColor) ? "#FFFFFF" : IconColor.Trim(),
            ItemTypes = GetSelectedTypes().Count == 0 ? null : GetSelectedTypes().OrderBy(static value => value).ToArray()
        };
    }

    public static StatusVariantItemViewModel FromDocument(StatusVariantDocument document)
    {
        var vm = new StatusVariantItemViewModel
        {
            Name = document.Name,
            IconGlyph = document.IconGlyph,
            IconColor = string.IsNullOrWhiteSpace(document.IconColor) ? "#FFFFFF" : document.IconColor
        };

        var initialTypes = (document.ItemTypes ?? [])
            .Distinct()
            .ToArray();

        foreach (var type in initialTypes)
        {
            vm.SetTypeSelected(type, true);
        }

        vm.OnPropertyChanged(nameof(SelectedItemTypesDisplayText));
        return vm;
    }

    private HashSet<CatalogDocumentType> GetSelectedTypes()
    {
        return TypeOptions
            .Where(static option => option.IsSelected)
            .Where(optionTypes.ContainsKey)
            .Select(option => optionTypes[option])
            .ToHashSet();
    }

    private static string GetTypeLabel(CatalogDocumentType type) =>
        LocalizationService.Current.Get(ExtraKeys.TypePrefix + type);

    private void RefreshLocalization()
    {
        foreach (var (option, type) in optionTypes)
        {
            option.Label = GetTypeLabel(type);
        }

        OnPropertyChanged(nameof(SelectedItemTypesDisplayText));
    }
}
