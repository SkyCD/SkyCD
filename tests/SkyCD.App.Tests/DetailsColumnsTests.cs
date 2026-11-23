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
    public void Defaults_ShowNameSizeAndLocation()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var headers = vm.BrowserDetailsColumns.Select(column => column.Header).ToArray();

        Assert.Equal(
        [
            LocalizationService.Current.Get(TextKeys.Name2),
            LocalizationService.Current.Get(TextKeys.Size),
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
            DetailsColumnsCatalog.StatusId
        ], stored.Select(setting => setting.Id));
        Assert.True(stored.Single(setting => setting.Id == DetailsColumnsCatalog.NameId).IsVisible);
        Assert.DoesNotContain(stored, setting => setting.Id == "unknown-column");

        var headers = vm.BrowserDetailsColumns.Select(column => column.Header).ToArray();
        Assert.Equal(
        [
            LocalizationService.Current.Get(UiKeys.Location),
            LocalizationService.Current.Get(TextKeys.Name2),
            MnemonicText.Strip(LocalizationService.Current.Get(TextKeys.Type2)),
            LocalizationService.Current.Get(TextKeys.Size)
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

        Assert.Equal(DetailsColumnsCatalog.LocationId, dialog.DetailsColumns[3].Id);
        Assert.Equal(DetailsColumnsCatalog.StatusId, dialog.DetailsColumns[4].Id);

        var settings = dialog.GetDetailsColumns();
        Assert.False(settings.Single(setting => setting.Id == DetailsColumnsCatalog.SizeId).IsVisible);
        Assert.True(settings.Single(setting => setting.Id == DetailsColumnsCatalog.NameId).IsVisible);
        Assert.Equal(DetailsColumnsCatalog.StatusId, settings[4].Id);
    }

    [Fact]
    public void OptionsDialog_DetailsColumnsCategoryShowsSection()
    {
        var dialog = new OptionsDialogViewModel(["English"]);
        var categories = dialog.SettingCategories.ToList();

        dialog.SelectedTabIndex = categories.FindIndex(
            category => category.Title == LocalizationService.Current.Get(UiKeys.DetailsColumns));

        Assert.True(dialog.IsDetailsColumnsCategorySelected);
        Assert.True(dialog.ShowDetailsColumnsSection);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.DetailsColumns), dialog.CurrentCategoryName);
    }

    [Fact]
    public void MoveDetailsColumn_ReordersVisibleColumnsAndKeepsHiddenColumnsInTheirSlots()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        vm.ApplyDetailsColumnSettings(
        [
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.NameId, IsVisible = true },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.SizeId, IsVisible = true },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.TypeId, IsVisible = false, Width = 175 },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.StatusId, IsVisible = false },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.LocationId, IsVisible = true }
        ]);
        var raised = 0;
        vm.DetailsColumnsChanged += (_, _) => raised++;

        var changed = vm.MoveDetailsColumn(DetailsColumnsCatalog.LocationId, 1);

        Assert.True(changed);
        Assert.Equal(1, raised);
        Assert.Equal(
        [
            DetailsColumnsCatalog.NameId,
            DetailsColumnsCatalog.LocationId,
            DetailsColumnsCatalog.SizeId
        ], vm.BrowserDetailsColumns.Select(column => column.Id));
        Assert.Equal(LocalizationService.Current.Get(TextKeys.Name2), vm.BrowserDetailsColumns[0].Header);

        var stored = vm.GetDetailsColumnSettings();
        Assert.Equal(
        [
            DetailsColumnsCatalog.NameId,
            DetailsColumnsCatalog.LocationId,
            DetailsColumnsCatalog.TypeId,
            DetailsColumnsCatalog.StatusId,
            DetailsColumnsCatalog.SizeId
        ], stored.Select(setting => setting.Id));
        Assert.False(stored.Single(setting => setting.Id == DetailsColumnsCatalog.TypeId).IsVisible);
        Assert.Equal(175, stored.Single(setting => setting.Id == DetailsColumnsCatalog.TypeId).Width);
    }

    [Fact]
    public void MoveDetailsColumn_MovesRequiredNameColumnAndIgnoresUnknownColumns()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var raised = 0;
        vm.DetailsColumnsChanged += (_, _) => raised++;

        Assert.False(vm.MoveDetailsColumn("unknown-column", 1));
        Assert.Equal(0, raised);

        Assert.True(vm.MoveDetailsColumn(DetailsColumnsCatalog.NameId, 2));

        Assert.Equal(1, raised);
        Assert.Equal(
        [
            DetailsColumnsCatalog.SizeId,
            DetailsColumnsCatalog.LocationId,
            DetailsColumnsCatalog.NameId
        ], vm.BrowserDetailsColumns.Select(column => column.Id));
        Assert.True(vm.GetDetailsColumnSettings()
            .Single(setting => setting.Id == DetailsColumnsCatalog.NameId).IsVisible);

        Assert.True(vm.MoveDetailsColumn(DetailsColumnsCatalog.NameId, -5));

        Assert.Equal(DetailsColumnsCatalog.NameId, vm.BrowserDetailsColumns[0].Id);
    }

    [Fact]
    public void MoveDetailsColumn_NameOrderSurvivesReopen()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        Assert.True(vm.MoveDetailsColumn(DetailsColumnsCatalog.NameId, 2));
        Assert.True(vm.MoveDetailsColumn(DetailsColumnsCatalog.LocationId, 0));
        var saved = vm.GetDetailsColumnSettings();

        using var reopened = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        reopened.ApplyDetailsColumnSettings(saved);

        Assert.Equal(
        [
            DetailsColumnsCatalog.LocationId,
            DetailsColumnsCatalog.SizeId,
            DetailsColumnsCatalog.NameId
        ], reopened.BrowserDetailsColumns.Select(column => column.Id));
        Assert.True(reopened.BrowserDetailsColumns
            .Single(column => column.Id == DetailsColumnsCatalog.NameId).ShowsItemIcon);
    }

    [Fact]
    public void OptionsDialog_CanMoveRequiredNameColumn()
    {
        var dialog = new OptionsDialogViewModel(["English"]);
        dialog.SelectedDetailsColumn = dialog.DetailsColumns.Single(item => item.Id == DetailsColumnsCatalog.NameId);

        Assert.False(dialog.MoveDetailsColumnUpCommand.CanExecute(null));
        Assert.True(dialog.MoveDetailsColumnDownCommand.CanExecute(null));

        dialog.MoveDetailsColumnDownCommand.Execute(null);

        Assert.Equal(DetailsColumnsCatalog.NameId, dialog.DetailsColumns[1].Id);
        Assert.Equal(1, dialog.GetDetailsColumns().ToList()
            .FindIndex(setting => setting.Id == DetailsColumnsCatalog.NameId));
    }

    [Fact]
    public void ResizeDetailsColumn_ClampsStoresAndSurvivesReopenRoundTrip()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var raised = 0;
        vm.DetailsColumnsChanged += (_, _) => raised++;

        Assert.True(vm.ResizeDetailsColumn(DetailsColumnsCatalog.SizeId, 240));

        Assert.Equal(1, raised);
        Assert.Equal(240, vm.GetDetailsColumnSettings()
            .Single(setting => setting.Id == DetailsColumnsCatalog.SizeId).Width);
        var sizeColumn = vm.BrowserDetailsColumns.Single(column => column.Id == DetailsColumnsCatalog.SizeId);
        Assert.Equal(new GridLength(240, GridUnitType.Pixel), sizeColumn.Width);

        Assert.True(vm.ResizeDetailsColumn(DetailsColumnsCatalog.LocationId, 5));
        Assert.Equal(DetailsColumnsCatalog.MinSavedColumnWidth, vm.GetDetailsColumnSettings()
            .Single(setting => setting.Id == DetailsColumnsCatalog.LocationId).Width);
        Assert.True(vm.ResizeDetailsColumn(DetailsColumnsCatalog.LocationId, 99999));
        Assert.Equal(DetailsColumnsCatalog.MaxSavedColumnWidth, vm.GetDetailsColumnSettings()
            .Single(setting => setting.Id == DetailsColumnsCatalog.LocationId).Width);

        Assert.False(vm.ResizeDetailsColumn(DetailsColumnsCatalog.SizeId, 240));
        Assert.False(vm.ResizeDetailsColumn("unknown-column", 300));
        Assert.Equal(3, raised);

        var saved = vm.GetDetailsColumnSettings();

        using var reopened = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        reopened.ApplyDetailsColumnSettings(saved);

        Assert.Equal(new GridLength(240, GridUnitType.Pixel),
            reopened.BrowserDetailsColumns.Single(column => column.Id == DetailsColumnsCatalog.SizeId).Width);
        Assert.Equal(new GridLength(DetailsColumnsCatalog.MaxSavedColumnWidth, GridUnitType.Pixel),
            reopened.BrowserDetailsColumns.Single(column => column.Id == DetailsColumnsCatalog.LocationId).Width);
        Assert.Equal(new GridLength(500, GridUnitType.Pixel),
            reopened.BrowserDetailsColumns.Single(column => column.Id == DetailsColumnsCatalog.NameId).Width);
    }

    [Fact]
    public void ResizeDetailsColumn_KeepsHiddenColumnWidthAcrossHideAndShow()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        vm.ResizeDetailsColumn(DetailsColumnsCatalog.SizeId, 180);
        vm.ToggleDetailsColumnCommand.Execute(DetailsColumnsCatalog.SizeId);

        Assert.DoesNotContain(vm.BrowserDetailsColumns, column => column.Id == DetailsColumnsCatalog.SizeId);
        Assert.Equal(180, vm.GetDetailsColumnSettings()
            .Single(setting => setting.Id == DetailsColumnsCatalog.SizeId).Width);

        vm.ToggleDetailsColumnCommand.Execute(DetailsColumnsCatalog.SizeId);

        var sizeColumn = vm.BrowserDetailsColumns.Single(column => column.Id == DetailsColumnsCatalog.SizeId);
        Assert.Equal(new GridLength(180, GridUnitType.Pixel), sizeColumn.Width);
    }

    [Fact]
    public void Normalize_PreservesSavedWidthsAndDropsInvalidOnes()
    {
        var normalized = DetailsColumnsCatalog.Normalize(
        [
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.SizeId, IsVisible = false, Width = 222 },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.TypeId, IsVisible = true, Width = 0 },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.StatusId, IsVisible = true, Width = -10 }
        ]);

        Assert.Equal(222, normalized.Single(setting => setting.Id == DetailsColumnsCatalog.SizeId).Width);
        Assert.Null(normalized.Single(setting => setting.Id == DetailsColumnsCatalog.TypeId).Width);
        Assert.Null(normalized.Single(setting => setting.Id == DetailsColumnsCatalog.StatusId).Width);
        Assert.Null(normalized.Single(setting => setting.Id == DetailsColumnsCatalog.LocationId).Width);
    }

    [Fact]
    public void OptionsDialog_RoundTripsColumnOrderAndWidth()
    {
        var dialog = new OptionsDialogViewModel(["English"]);
        dialog.SetDetailsColumns(
        [
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.NameId, IsVisible = true, Width = 640 },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.SizeId, IsVisible = false, Width = 130 },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.TypeId, IsVisible = true },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.StatusId, IsVisible = false },
            new DetailsColumnSettingDocument { Id = DetailsColumnsCatalog.LocationId, IsVisible = true }
        ]);

        var settings = dialog.GetDetailsColumns();

        Assert.Equal(
        [
            DetailsColumnsCatalog.NameId,
            DetailsColumnsCatalog.SizeId,
            DetailsColumnsCatalog.TypeId,
            DetailsColumnsCatalog.StatusId,
            DetailsColumnsCatalog.LocationId
        ], settings.Select(setting => setting.Id));
        Assert.Equal(640, settings.Single(setting => setting.Id == DetailsColumnsCatalog.NameId).Width);
        Assert.Equal(130, settings.Single(setting => setting.Id == DetailsColumnsCatalog.SizeId).Width);
        Assert.Null(settings.Single(setting => setting.Id == DetailsColumnsCatalog.TypeId).Width);
        Assert.False(settings.Single(setting => setting.Id == DetailsColumnsCatalog.SizeId).IsVisible);
    }

    [Fact]
    public void OptionsDialog_LocationsCategoryShowsSection()
    {
        var dialog = new OptionsDialogViewModel(["English"]);
        var categories = dialog.SettingCategories.ToList();

        var locationsIndex = categories.FindIndex(
            category => category.Title == LocalizationService.Current.Get(UiKeys.Locations));
        dialog.SelectedTabIndex = locationsIndex;

        Assert.True(dialog.IsLocationsCategorySelected);
        Assert.True(dialog.ShowLocationsSection);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.Locations), dialog.CurrentCategoryName);
        Assert.True(locationsIndex > categories.FindIndex(
            category => category.Title == LocalizationService.Current.Get(UiKeys.DetailsColumns)));
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
