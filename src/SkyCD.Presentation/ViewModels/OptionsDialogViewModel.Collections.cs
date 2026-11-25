using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    public const string CollectionsCategoryId = "collections";

    // Index 8 is the unused slot between the scan group and the toolbar page; plugin pages use -1.
    private const int CollectionsCategoryIndex = 8;

    public CollectionsDialogViewModel? Collections { get; init; }

    public bool ShowCollectionsSection =>
        SelectedBuiltInIndex == CollectionsCategoryIndex && IsCurrentCategoryVisibleInSearch;

    public OptionsDialogViewModel(CollectionsDialogViewModel? collections)
        : this(LocalizationService.Current)
    {
        Collections = collections;
    }
}
