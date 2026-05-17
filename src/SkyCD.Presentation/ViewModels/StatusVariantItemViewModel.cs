using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Documents;
using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.ViewModels;

public partial class StatusVariantItemViewModel : ObservableObject
{
    public static IReadOnlyList<CatalogDocumentType> AvailableItemTypes { get; } =
        Enum.GetValues<CatalogDocumentType>();

    [ObservableProperty] private string name = string.Empty;

    [ObservableProperty] private string iconGlyph = string.Empty;

    [ObservableProperty] private bool isDropHintVisible;

    [ObservableProperty] private CatalogDocumentType? itemType;

    public StatusVariantDocument ToDocument()
    {
        return new StatusVariantDocument
        {
            Name = Name.Trim(),
            IconGlyph = IconGlyph.Trim(),
            ItemType = ItemType
        };
    }

    public static StatusVariantItemViewModel FromDocument(StatusVariantDocument document)
    {
        return new StatusVariantItemViewModel
        {
            Name = document.Name,
            IconGlyph = document.IconGlyph,
            ItemType = document.ItemType
        };
    }
}
