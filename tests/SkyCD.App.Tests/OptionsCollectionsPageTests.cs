using System.Linq;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class OptionsCollectionsPageTests
{
    [Fact]
    public void SettingCategories_ListsCollectionsNextToCustomProperties()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        var categories = vm.SettingCategories.ToList();
        var index = categories.FindIndex(category => category.Id == OptionsDialogViewModel.CollectionsCategoryId);

        Assert.True(index >= 0);
        Assert.Equal("custom-properties", categories[index - 1].Id);
        Assert.Equal("scan", categories[index + 1].Id);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.Collections), categories[index].Title);
        Assert.Equal(8, categories[index].BuiltInIndex);
    }

    [Fact]
    public void SelectingCollectionsCategory_ShowsCollectionsSectionOnly()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SelectedSettingCategory = vm.SettingCategories.Single(
            category => category.Id == OptionsDialogViewModel.CollectionsCategoryId);

        Assert.True(vm.ShowCollectionsSection);
        Assert.False(vm.ShowStatusesSection);
        Assert.False(vm.ShowCustomPropertiesSection);
        Assert.True(vm.HasVisibleCategoryContent);
    }

    [Fact]
    public void RestoreSettingCategory_ResolvesCollectionsById()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.RestoreSettingCategory(OptionsDialogViewModel.CollectionsCategoryId, 0);

        Assert.Equal(OptionsDialogViewModel.CollectionsCategoryId, vm.SelectedSettingCategory?.Id);
        Assert.Equal(OptionsDialogViewModel.CollectionsCategoryId, vm.SelectedSidebarEntry?.Category?.Id);
        Assert.True(vm.ShowCollectionsSection);
    }

    [Fact]
    public void SearchCollections_KeepsCollectionsCategorySelectable()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SettingsSearchText = "collections";

        var category = Assert.Single(vm.FilteredSettingCategories);
        Assert.Equal(OptionsDialogViewModel.CollectionsCategoryId, category.Id);
        Assert.True(vm.ShowCollectionsSection);
    }

    [Fact]
    public void CollectionsProperty_HostsTheProvidedViewModel()
    {
        var collections = new CollectionsDialogViewModel();
        var vm = new OptionsDialogViewModel(collections);

        Assert.Same(collections, vm.Collections);
        Assert.False(vm.Collections!.CreateCollectionCommand.CanExecute(null));
    }

    [Fact]
    public void Dispose_StopsWatchingCollectionsWithoutAViewModel()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.Dispose();
    }
}
