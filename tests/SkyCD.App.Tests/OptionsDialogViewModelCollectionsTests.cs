using System;
using System.Linq;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class OptionsDialogViewModelCollectionsTests
{
    [Fact]
    public void CollectionsCategory_IsAbsentWithoutHostedPage()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);

        Assert.DoesNotContain(viewModel.SettingCategories, category => category.Id == OptionsDialogViewModel.CollectionsCategoryId);
        Assert.DoesNotContain(viewModel.SidebarEntries, entry => entry.Category?.Id == OptionsDialogViewModel.CollectionsCategoryId);
        Assert.False(viewModel.ShowCollectionsSection);
    }

    [Fact]
    public void CollectionsCategory_AppearsNextToCustomPropertiesWithTranslatedTitle()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);

        viewModel.SetCollections(new CollectionsDialogViewModel());

        var ids = viewModel.SettingCategories.Select(category => category.Id).ToArray();
        Assert.Contains(OptionsDialogViewModel.CollectionsCategoryId, ids);
        Assert.Equal(Array.IndexOf(ids, "custom-properties") + 1,
            Array.IndexOf(ids, OptionsDialogViewModel.CollectionsCategoryId));
        var category = viewModel.SettingCategories.Single(item => item.Id == OptionsDialogViewModel.CollectionsCategoryId);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.Collections), category.Title);
        Assert.Contains(viewModel.SidebarEntries, entry => entry.Category?.Id == OptionsDialogViewModel.CollectionsCategoryId);
    }

    [Fact]
    public void SettingUpPage_KeepsCurrentSelection()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SelectedSettingCategory = viewModel.SettingCategories.Single(category => category.Id == "language");

        viewModel.SetCollections(new CollectionsDialogViewModel());

        Assert.Equal("language", viewModel.SelectedSettingCategory?.Id);
    }

    [Fact]
    public void SelectingCollectionsCategory_ShowsSectionAndCountsAsContent()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetCollections(new CollectionsDialogViewModel());

        viewModel.SelectedSettingCategory = viewModel.SettingCategories
            .Single(category => category.Id == OptionsDialogViewModel.CollectionsCategoryId);

        Assert.True(viewModel.IsCollectionsCategorySelected);
        Assert.True(viewModel.ShowCollectionsSection);
        Assert.True(viewModel.HasVisibleCategoryContent);
        Assert.False(viewModel.ShowNoSearchResults);
        Assert.False(viewModel.ShowCustomPropertiesSection);
    }

    [Fact]
    public void RestoreSettingCategory_ResolvesCollectionsById()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetCollections(new CollectionsDialogViewModel());

        viewModel.RestoreSettingCategory(OptionsDialogViewModel.CollectionsCategoryId, legacyTabIndex: 0);

        Assert.Equal(OptionsDialogViewModel.CollectionsCategoryId, viewModel.SelectedSettingCategory?.Id);
        Assert.Equal(OptionsDialogViewModel.CollectionsCategoryId, viewModel.SelectedSidebarEntry?.Category?.Id);
    }

    [Fact]
    public void Search_UsesTheTranslatedCollectionsTitle()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetCollections(new CollectionsDialogViewModel());

        viewModel.SettingsSearchText = LocalizationService.Current.Get(UiKeys.Collections);

        Assert.Contains(viewModel.FilteredSettingCategories,
            category => category.Id == OptionsDialogViewModel.CollectionsCategoryId);
        Assert.Equal(OptionsDialogViewModel.CollectionsCategoryId, viewModel.SelectedSettingCategory?.Id);
        Assert.True(viewModel.ShowCollectionsSection);
    }

    [Fact]
    public void SearchWithoutMatch_HidesHostedCollectionsSection()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetCollections(new CollectionsDialogViewModel());
        viewModel.SelectedSettingCategory = viewModel.SettingCategories
            .Single(category => category.Id == OptionsDialogViewModel.CollectionsCategoryId);

        viewModel.SettingsSearchText = "zzzz-no-match";

        Assert.False(viewModel.ShowCollectionsSection);
        Assert.True(viewModel.ShowNoSearchResults);
    }

    [Fact]
    public void CollectionsPageKeys_AreTranslatedInLithuanian()
    {
        var service = new LocalizationService();
        var english = System.Globalization.CultureInfo.GetCultureInfo("en");
        var lithuanian = System.Globalization.CultureInfo.GetCultureInfo("lt");
        var keys = new[]
        {
            UiKeys.Collections,
            UiKeys.ManageCollections,
            UiKeys.NewCollection,
            UiKeys.CollectionName,
            UiKeys.RenameCollection,
            UiKeys.DeleteCollection,
            UiKeys.RemoveMissing,
            UiKeys.GoToEntry,
            UiKeys.CollectionMembers,
            UiKeys.CollectionEntries,
            UiKeys.NoCollections,
            TextKeys.CollectionNameRequired,
            TextKeys.CollectionCreated,
            TextKeys.CollectionRenamed,
            TextKeys.CollectionDeleted,
            TextKeys.CollectionCleaned
        };

        foreach (var key in keys)
        {
            var en = service.Get(key, english);
            var lt = service.Get(key, lithuanian);
            Assert.NotEqual(key, en);
            Assert.NotEqual(key, lt);
            Assert.NotEqual(en, lt);
        }
    }
}
