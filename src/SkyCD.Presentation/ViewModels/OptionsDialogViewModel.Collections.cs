using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

public partial class OptionsDialogViewModel
{
    public const string CollectionsCategoryId = "collections";
    private const int CollectionsCategoryIndex = 8;

    [ObservableProperty]
    private CollectionsDialogViewModel? collectionsPage;

    public bool IsCollectionsCategorySelected => SelectedBuiltInIndex == CollectionsCategoryIndex;

    public bool ShowCollectionsSection =>
        IsCollectionsCategorySelected &&
        IsCurrentCategoryVisibleInSearch &&
        CollectionsPage is not null;

    public void SetCollections(CollectionsDialogViewModel page)
    {
        CollectionsPage = page;
        RefreshCategoriesPreservingSelection();
        NotifySettingsVisibilityChanged();
    }

    partial void OnCollectionsPageChanged(CollectionsDialogViewModel? value)
    {
        OnPropertyChanged(nameof(ShowCollectionsSection));
    }
}
