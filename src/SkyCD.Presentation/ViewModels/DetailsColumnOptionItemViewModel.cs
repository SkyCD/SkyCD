using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public sealed partial class DetailsColumnOptionItemViewModel : ObservableObject
{
    public required string Id { get; init; }

    [ObservableProperty] private string header = string.Empty;

    [ObservableProperty] private bool isVisible = true;

    public bool IsRequired { get; init; }

    /// <summary>Saved pixel width, or <c>null</c> when the column uses its catalog default.</summary>
    public double? Width { get; set; }

    public bool IsRemovable => !IsRequired;

    public string RequiredLabel => LocalizationService.Current.Get(UiKeys.RequiredColumn);

    public void RefreshLocalization(string header)
    {
        Header = header;
        OnPropertyChanged(nameof(RequiredLabel));
    }
}
