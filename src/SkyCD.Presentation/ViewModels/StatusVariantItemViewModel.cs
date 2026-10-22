using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.UI.Controls.Selectors.MultiSelectDropdown;
using Avalonia.Media;

namespace SkyCD.Presentation.ViewModels;

public partial class StatusVariantItemViewModel : ObservableObject
{
    public static IReadOnlyList<CatalogDocumentType> AvailableItemTypes { get; } =
        Enum.GetValues<CatalogDocumentType>();

    [ObservableProperty] private string name = string.Empty;

    [ObservableProperty] private string iconGlyph = string.Empty;
    [ObservableProperty] private string iconColor = "#FFFFFF";

    [ObservableProperty] private bool isDropHintVisible;

    public ObservableCollection<MultiSelectOptionItem> TypeOptions { get; } = [];
    public ObservableCollection<MultiSelectOptionItem> IconOptionItems { get; } = [];
    public IReadOnlyList<string> IconOptions { get; } = StatusIconCatalog.GetAllKeys();
    public IReadOnlyList<string> ColorOptions { get; } =
    [
        "#000000", "#44546A", "#5B9BD5", "#70AD47", "#A5A5A5", "#FFC000", "#4472C4", "#ED7D31",
        "#1F1F1F", "#2F3A4D", "#2E75B5", "#548235", "#7F7F7F", "#BF9000", "#2F5597", "#C55A11",
        "#595959", "#8497B0", "#9DC3E6", "#A9D18E", "#C9C9C9", "#FFE699", "#8FAADC", "#F4B183",
        "#D9E1F2", "#E2EFDA", "#FCE4D6", "#FFF2CC", "#EDEDED", "#D6E3F3", "#E2F0D9", "#FBE5D6"
    ];

    public object? IconKind => StatusIconCatalog.TryResolveKind(IconGlyph, out var kind) ? kind : null;
    public IBrush IconBrush => StatusIconCatalog.ResolveBrush(IconColor);

    public StatusVariantItemViewModel()
    {
        foreach (var availableType in AvailableItemTypes)
        {
            var option = new MultiSelectOptionItem
            {
                Label = availableType.ToString()
            };
            option.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MultiSelectOptionItem.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedItemTypesDisplayText));
                }
            };
            TypeOptions.Add(option);
        }

        foreach (var iconKey in IconOptions)
        {
            var option = new MultiSelectOptionItem
            {
                Label = iconKey
            };
            option.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(MultiSelectOptionItem.IsSelected) || !option.IsSelected)
                {
                    return;
                }

                foreach (var candidate in IconOptionItems)
                {
                    if (!ReferenceEquals(candidate, option) && candidate.IsSelected)
                    {
                        candidate.IsSelected = false;
                    }
                }

                if (!string.Equals(IconGlyph, option.Label, StringComparison.Ordinal))
                {
                    IconGlyph = option.Label;
                }
            };
            IconOptionItems.Add(option);
        }
    }

    public string SelectedItemTypesDisplayText => GetSelectedTypes().Count == 0
        ? string.Empty
        : string.Join(", ", GetSelectedTypes().OrderBy(static value => value).Select(static value => value.ToString()));

    public bool IsTypeSelected(CatalogDocumentType type) => GetSelectedTypes().Contains(type);

    public void SetTypeSelected(CatalogDocumentType type, bool isSelected)
    {
        var option = TypeOptions.FirstOrDefault(candidate =>
            string.Equals(candidate.Label, type.ToString(), StringComparison.Ordinal));
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
            var option = vm.TypeOptions.FirstOrDefault(candidate =>
                string.Equals(candidate.Label, type.ToString(), StringComparison.Ordinal));
            if (option is not null)
            {
                option.IsSelected = true;
            }
        }

        vm.OnPropertyChanged(nameof(SelectedItemTypesDisplayText));
        vm.OnPropertyChanged(nameof(IconKind));
        vm.OnPropertyChanged(nameof(IconBrush));
        return vm;
    }

    partial void OnIconGlyphChanged(string value)
    {
        foreach (var option in IconOptionItems)
        {
            option.IsSelected = string.Equals(option.Label, value, StringComparison.Ordinal);
        }

        OnPropertyChanged(nameof(IconKind));
    }

    partial void OnIconColorChanged(string value)
    {
        OnPropertyChanged(nameof(IconBrush));
    }

    private HashSet<CatalogDocumentType> GetSelectedTypes()
    {
        return TypeOptions
            .Where(static option => option.IsSelected)
            .Select(option => Enum.TryParse<CatalogDocumentType>(option.Label, out var parsed) ? parsed : (CatalogDocumentType?)null)
            .Where(static type => type.HasValue)
            .Select(static type => type!.Value)
            .ToHashSet();
    }
}
