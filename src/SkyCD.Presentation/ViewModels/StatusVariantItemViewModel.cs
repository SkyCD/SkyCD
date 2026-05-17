using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Documents;

namespace SkyCD.Presentation.ViewModels;

public partial class StatusVariantItemViewModel : ObservableObject
{
    [ObservableProperty] private string name = string.Empty;

    [ObservableProperty] private string iconGlyph = string.Empty;

    public StatusVariantDocument ToDocument()
    {
        return new StatusVariantDocument
        {
            Name = Name.Trim(),
            IconGlyph = IconGlyph.Trim()
        };
    }

    public static StatusVariantItemViewModel FromDocument(StatusVariantDocument document)
    {
        return new StatusVariantItemViewModel
        {
            Name = document.Name,
            IconGlyph = document.IconGlyph
        };
    }
}
