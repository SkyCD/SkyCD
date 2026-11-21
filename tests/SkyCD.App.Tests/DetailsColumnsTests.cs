using System.Linq;
using Avalonia.Controls;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class DetailsColumnsTests
{
    [Fact]
    public void Defaults_ShowNameSizeLentToAndLocation()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var headers = vm.BrowserDetailsColumns.Select(column => column.Header).ToArray();

        Assert.Equal(
        [
            LocalizationService.Current.Get(TextKeys.Name2),
            LocalizationService.Current.Get(TextKeys.Size),
            LocalizationService.Current.Get(UiKeys.LentTo),
            LocalizationService.Current.Get(UiKeys.Location)
        ], headers);
    }

    [Fact]
    public void ToggleDetailsColumn_HidesColumnAndRaisesChangeEvent()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var raised = 0;
        vm.DetailsColumnsChanged += (_, _) => raised++;

        vm.ToggleDetailsColumnCommand.Execute(DetailsColumnsCatalog.SizeId);

        Assert.Equal(1, raised);
        Assert.DoesNotContain(vm.BrowserDetailsColumns,
            column => column.Header == LocalizationService.Current.Get(TextKeys.Size));
        Assert.False(vm.GetDetailsColumnSettings()
            .Single(setting => setting.Id == DetailsColumnsCatalog.SizeId).IsVisible);
    }

    [Fact]
    public void ToggleDetailsColumn_KeepsRequiredNameColumnVisible()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var raised = false;
        vm.DetailsColumnsChanged += (_, _) => raised = true;

        vm.ToggleDetailsColumnCommand.Execute(DetailsColumnsCatalog.NameId);

        Assert.False(raised);
        Assert.Contains(vm.BrowserDetailsColumns,
            column => column.Header == LocalizationService.Current.Get(TextKeys.Name2));
        Assert.True(vm.GetDetailsColumnSettings()
            .Single(setting => setting.Id == DetailsColumnsCatalog.NameId).IsVisible);
    }

    [Fact]
    public void ApplyDetailsColumnSettings_PreservesOrderDropsUnknownAndForcesRequiredVisible()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        vm.ApplyDetailsColumnSettings(
        [
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.LocationId, IsVisible = true },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.NameId, IsVisible = false },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.TypeId, IsVisible = true },
            new DetailsColumnSettingDocument { Id = "unknown-column", IsVisible = true }
        ]);

        var stored = vm.GetDetailsColumnSettings();
        Assert.Equal(
        [
            DetailsColumnsCatalog.LocationId,
            DetailsColumnsCatalog.NameId,
            DetailsColumnsCatalog.TypeId,
            DetailsColumnsCatalog.SizeId,
            DetailsColumnsCatalog.StatusId,
            DetailsColumnsCatalog.LentToId
        ], stored.Select(setting => setting.Id));
        Assert.True(stored.Single(setting => setting.Id == DetailsColumnsCatalog.NameId).IsVisible);
        Assert.DoesNotContain(stored, setting => setting.Id == "unknown-column");

        var headers = vm.BrowserDetailsColumns.Select(column => column.Header).ToArray();
        Assert.Equal(
        [
            LocalizationService.Current.Get(UiKeys.Location),
            LocalizationService.Current.Get(TextKeys.Name2),
            MnemonicText.Strip(LocalizationService.Current.Get(TextKeys.Type2)),
            LocalizationService.Current.Get(TextKeys.Size),
            LocalizationService.Current.Get(UiKeys.LentTo)
        ], headers);
    }

    [Fact]
    public void HeaderMenu_ReflectsVisibilityAndDisablesRequiredColumn()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var nameItem = vm.DetailsHeaderMenuItems.Single(item => item.Key == "details_column_" + DetailsColumnsCatalog.NameId);
        var sizeItem = vm.DetailsHeaderMenuItems.Single(item => item.Key == "details_column_" + DetailsColumnsCatalog.SizeId);
        var configureItem = vm.DetailsHeaderMenuItems.Single(item => item.Key == "details_columns_options");

        Assert.True(nameItem.IsChecked);
        Assert.False(nameItem.IsEnabled);
        Assert.Equal(
            LocalizationService.Current.Format(UiKeys.DetailsColumnRequiredHeader,
                LocalizationService.Current.Get(TextKeys.Name2)),
            nameItem.Header);
        Assert.Equal(MenuItemToggleType.CheckBox, nameItem.ToggleType);
        Assert.True(sizeItem.IsChecked);
        Assert.True(sizeItem.IsEnabled);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.ConfigureColumns), configureItem.Header);

        vm.ToggleDetailsColumnCommand.Execute(DetailsColumnsCatalog.SizeId);

        Assert.False(sizeItem.IsChecked);
    }

    [Fact]
    public void OpenDetailsColumnsOptions_PreselectsDetailsColumnsCategory()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        OptionsDialogRequestedEventArgs? request = null;
        vm.OptionsRequested += (_, args) => request = args;

        vm.OpenDetailsColumnsOptionsCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.DetailsColumns), request!.PreselectSettingCategory);
    }

    [Fact]
    public void OptionsDialog_RoundTripsColumnVisibilityAndOrder()
    {
        var dialog = new OptionsDialogViewModel(["English"]);

        var name = dialog.DetailsColumns.Single(item => item.Id == DetailsColumnsCatalog.NameId);
        Assert.True(name.IsRequired);
        Assert.False(name.IsRemovable);
        Assert.True(name.IsVisible);

        dialog.DetailsColumns.Single(item => item.Id == DetailsColumnsCatalog.SizeId).IsVisible = false;
        dialog.SelectedDetailsColumn = dialog.DetailsColumns.Single(item => item.Id == DetailsColumnsCatalog.LocationId);

        Assert.True(dialog.MoveDetailsColumnUpCommand.CanExecute(null));
        dialog.MoveDetailsColumnUpCommand.Execute(null);

        Assert.Equal(DetailsColumnsCatalog.LocationId, dialog.DetailsColumns[4].Id);
        Assert.Equal(DetailsColumnsCatalog.LentToId, dialog.DetailsColumns[5].Id);

        var settings = dialog.GetDetailsColumns();
        Assert.False(settings.Single(setting => setting.Id == DetailsColumnsCatalog.SizeId).IsVisible);
        Assert.True(settings.Single(setting => setting.Id == DetailsColumnsCatalog.NameId).IsVisible);
        Assert.Equal(DetailsColumnsCatalog.LocationId, settings[4].Id);
    }

    [Fact]
    public void OptionsDialog_DetailsColumnsCategoryShowsSection()
    {
        var dialog = new OptionsDialogViewModel(["English"]);
        var categories = dialog.SettingCategories.ToList();

        dialog.SelectedTabIndex = categories.IndexOf(LocalizationService.Current.Get(UiKeys.DetailsColumns));

        Assert.True(dialog.IsDetailsColumnsCategorySelected);
        Assert.True(dialog.ShowDetailsColumnsSection);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.DetailsColumns), dialog.CurrentCategoryName);
    }

    [Fact]
    public void OptionsDialog_LocationsCategoryShowsSection()
    {
        var dialog = new OptionsDialogViewModel(["English"]);
        var categories = dialog.SettingCategories.ToList();

        dialog.SelectedTabIndex = categories.IndexOf(LocalizationService.Current.Get(UiKeys.Locations));

        Assert.True(dialog.IsLocationsCategorySelected);
        Assert.True(dialog.ShowLocationsSection);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.Locations), dialog.CurrentCategoryName);
        Assert.True(categories.IndexOf(LocalizationService.Current.Get(UiKeys.Locations)) >
                    categories.IndexOf(LocalizationService.Current.Get(UiKeys.DetailsColumns)));
    }

    [Theory]
    [InlineData(DetailsColumnsCatalog.TypeId, TextKeys.Type2)]
    [InlineData(DetailsColumnsCatalog.StatusId, TextKeys.Status)]
    public void ColumnDefinition_BrowseHeaderDropsMnemonicMarkerButKeepsRawHeader(string id, string headerKey)
    {
        var definition = DetailsColumnsCatalog.Find(id);
        Assert.NotNull(definition);

        var localized = LocalizationService.Current.Get(headerKey);

        Assert.Equal(localized, definition!.Header);
        Assert.StartsWith("_", definition.Header);
        Assert.Equal(localized.TrimStart('_'), definition.CreateColumn().Header);
        Assert.DoesNotContain("_", definition.CreateColumn().Header);
        Assert.DoesNotContain("&", definition.CreateColumn().Header);
    }

    [Theory]
    [InlineData(DetailsColumnsCatalog.TypeId, TextKeys.Type2)]
    [InlineData(DetailsColumnsCatalog.StatusId, TextKeys.Status)]
    public void OptionsDialog_DetailsColumnHeadersHaveNoMnemonicMarkers(string id, string headerKey)
    {
        var dialog = new OptionsDialogViewModel(["English"]);

        var item = dialog.DetailsColumns.Single(column => column.Id == id);

        Assert.DoesNotContain("_", item.Header);
        Assert.DoesNotContain("&", item.Header);
        Assert.Equal(
            LocalizationService.Current.Get(headerKey).TrimStart('_'),
            item.Header);
    }

    [Fact]
    public void OptionsDialog_DetailsColumnHeadersHaveNoMnemonicMarkersForEveryColumn()
    {
        var dialog = new OptionsDialogViewModel(["English"]);

        Assert.All(dialog.DetailsColumns, item =>
        {
            Assert.DoesNotContain("_", item.Header);
            Assert.DoesNotContain("&", item.Header);
        });
    }

    [Fact]
    public void SetDetailsColumns_StripsMnemonicMarkers()
    {
        var dialog = new OptionsDialogViewModel(["English"]);

        dialog.SetDetailsColumns(
        [
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.StatusId, IsVisible = true },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.TypeId, IsVisible = true }
        ]);

        Assert.All(dialog.DetailsColumns, item => Assert.DoesNotContain("_", item.Header));
        Assert.Equal(
            LocalizationService.Current.Get(TextKeys.Status).TrimStart('_'),
            dialog.DetailsColumns.Single(item => item.Id == DetailsColumnsCatalog.StatusId).Header);
        Assert.Equal(
            LocalizationService.Current.Get(TextKeys.Type2).TrimStart('_'),
            dialog.DetailsColumns.Single(item => item.Id == DetailsColumnsCatalog.TypeId).Header);
    }

    [Fact]
    public void DetailsHeaderMenu_KeepsRawHeaderWithMnemonicMarker()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var typeItem = vm.DetailsHeaderMenuItems.Single(item => item.Key == "details_column_" + DetailsColumnsCatalog.TypeId);

        Assert.Equal(LocalizationService.Current.Get(TextKeys.Type2), typeItem.Header);
        Assert.StartsWith("_", typeItem.Header);
    }
}
