using System;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class ToolbarCustomizationTests
{
    [Fact]
    public void DefaultLayout_MatchesShippedToolbar()
    {
        var layout = ToolbarLayout.CreateDefault();

        Assert.Equal(
        [
            ToolbarCommandIds.NewCatalog,
            ToolbarCommandIds.OpenCatalog,
            ToolbarCommandIds.SaveCatalog,
            ToolbarItemDocument.SeparatorKind,
            ToolbarCommandIds.Find
        ], layout.Select(entry => entry.Kind == ToolbarItemDocument.SeparatorKind ? entry.Kind : entry.CommandId));
    }

    [Fact]
    public void Normalize_DropsUnknownAndDuplicateCommandsAndRepairsSeparators()
    {
        var layout = new[]
        {
            ToolbarItemDocument.Separator(),
            ToolbarItemDocument.Command(ToolbarCommandIds.Refresh),
            ToolbarItemDocument.Separator(),
            ToolbarItemDocument.Separator(),
            ToolbarItemDocument.Command(ToolbarCommandIds.Refresh),
            ToolbarItemDocument.Command("removed-command"),
            ToolbarItemDocument.Command(ToolbarCommandIds.Find),
            ToolbarItemDocument.Separator()
        };

        var normalized = ToolbarLayout.Normalize(layout, ToolbarCommandIds.All);

        Assert.Collection(normalized,
            entry => Assert.Equal(ToolbarCommandIds.Refresh, entry.CommandId),
            entry => Assert.Equal(ToolbarItemDocument.SeparatorKind, entry.Kind),
            entry => Assert.Equal(ToolbarCommandIds.Find, entry.CommandId));
    }

    [Fact]
    public void Normalize_AllStaleEntriesFallBackToDefault()
    {
        var normalized = ToolbarLayout.Normalize(
            [ToolbarItemDocument.Command("removed-command")], ToolbarCommandIds.All);

        Assert.Equal(
            ToolbarLayout.CreateDefault().Select(entry => entry.CommandId),
            normalized.Select(entry => entry.CommandId));
    }

    [Fact]
    public void Normalize_DeliberatelyEmptyLayoutStaysEmpty()
    {
        var normalized = ToolbarLayout.Normalize([], ToolbarCommandIds.All);

        Assert.Empty(normalized);
    }

    [Fact]
    public void Customization_AddRemoveReorderAndReset()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var dialog = new ToolbarCustomizationViewModel(
            vm.ToolbarCommands(),
            [ToolbarItemDocument.Command(ToolbarCommandIds.NewCatalog)]);

        var refresh = dialog.AvailableCommands.Single(choice => choice.Descriptor.Id == ToolbarCommandIds.Refresh);
        var find = dialog.AvailableCommands.Single(choice => choice.Descriptor.Id == ToolbarCommandIds.Find);
        Assert.False(refresh.IsInToolbar);

        dialog.SelectedCommand = refresh;
        dialog.AddCommand.Execute(null);
        dialog.SelectedCommand = find;
        dialog.AddCommand.Execute(null);

        Assert.Equal(
            [ToolbarCommandIds.NewCatalog, ToolbarCommandIds.Refresh, ToolbarCommandIds.Find],
            dialog.GetLayout().Select(entry => entry.CommandId));

        dialog.SelectedItem = dialog.SelectedItems[^1];
        dialog.AddSeparatorCommand.Execute(null);
        Assert.Equal(ToolbarItemDocument.SeparatorKind, dialog.GetLayout()[^1].Kind);

        dialog.SelectedItem = dialog.SelectedItems[1];
        dialog.MoveUpCommand.Execute(null);
        Assert.Equal(ToolbarCommandIds.Refresh, dialog.SelectedItems[0].CommandId);

        dialog.RemoveCommand.Execute(null);
        Assert.DoesNotContain(dialog.SelectedItems, entry => entry.CommandId == ToolbarCommandIds.Refresh);
        Assert.False(refresh.IsInToolbar);

        dialog.ResetCommand.Execute(null);
        Assert.Equal(
            ToolbarLayout.CreateDefault().Select(entry => entry.CommandId),
            dialog.GetLayout().Select(entry => entry.CommandId));
    }

    [Fact]
    public void Customization_CannotAddCommandTwice()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var dialog = new ToolbarCustomizationViewModel(
            vm.ToolbarCommands(),
            ToolbarLayout.CreateDefault());

        var newCatalog = dialog.AvailableCommands.Single(choice => choice.Descriptor.Id == ToolbarCommandIds.NewCatalog);
        Assert.True(newCatalog.IsInToolbar);

        dialog.SelectedCommand = newCatalog;
        Assert.False(dialog.AddCommand.CanExecute(null));
    }

    [Fact]
    public void MainWindowViewModel_LoadToolbarLayoutNormalizesAndNotifies()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var raised = 0;
        vm.ToolbarItemsChanged += (_, _) => raised++;

        vm.LoadToolbarLayout([ToolbarItemDocument.Command("removed-command")]);

        Assert.Equal(1, raised);
        Assert.Equal(
            ToolbarLayout.CreateDefault().Select(entry => entry.CommandId),
            vm.ToolbarEntries.Select(entry => entry.CommandId));
    }

    [Fact]
    public void MainWindowViewModel_ApplyToolbarLayoutRaisesLayoutChangedForPersistence()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var applied = 0;
        var persisted = 0;
        vm.ToolbarItemsChanged += (_, _) => applied++;
        vm.ToolbarLayoutChanged += (_, _) => persisted++;

        vm.ApplyToolbarLayout([ToolbarItemDocument.Command(ToolbarCommandIds.Options)]);

        Assert.Equal(1, applied);
        Assert.Equal(1, persisted);
        Assert.Equal(ToolbarCommandIds.Options, Assert.Single(vm.ToolbarEntries).CommandId);
    }

    [Fact]
    public void ToolbarCommands_OfferEveryKnownCommandIdOnce()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var commands = vm.ToolbarCommands();

        Assert.Equal(ToolbarCommandIds.All.Order(), commands.Select(command => command.Id).Order());
        Assert.All(commands, command => Assert.False(string.IsNullOrWhiteSpace(command.Label)));
        Assert.All(commands, command => Assert.False(string.IsNullOrWhiteSpace(command.Category)));
    }

    [Fact]
    public void CustomizeToolbarCommand_OpensOptionsOnToolbarCategory()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        OptionsDialogRequestedEventArgs? request = null;
        vm.OptionsRequested += (_, args) => request = args;

        vm.CustomizeToolbarCommand.Execute(null);

        Assert.NotNull(request);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.Toolbar), request.PreselectSettingCategory);
    }

    [Fact]
    public void OptionsDialog_ShowsToolbarCategoryAndPage()
    {
        var options = new OptionsDialogViewModel(["English"]);
        var toolbarCategory = LocalizationService.Current.Get(UiKeys.Toolbar);

        Assert.Contains(options.SettingCategories, category => category.Title == toolbarCategory);
        Assert.False(options.ShowToolbarSection);

        options.SelectedSettingCategory = options.SettingCategories.Single(
            category => category.Title == toolbarCategory);

        Assert.True(options.IsToolbarCategorySelected);
        Assert.True(options.ShowToolbarSection);
        Assert.True(options.HasVisibleCategoryContent);
    }

    [Fact]
    public void OptionsDialog_SearchFindsToolbarCategory()
    {
        var options = new OptionsDialogViewModel(["English"]);
        var toolbarCategory = LocalizationService.Current.Get(UiKeys.Toolbar);

        options.SettingsSearchText = "toolbar";

        Assert.Contains(options.FilteredSettingCategories, category => category.Title == toolbarCategory);
        Assert.Equal(toolbarCategory, options.SelectedSettingCategory?.Title);
        Assert.True(options.ShowToolbarSection);

        options.SettingsSearchText = "customize";

        Assert.Contains(options.FilteredSettingCategories, category => category.Title == toolbarCategory);
    }

    [Fact]
    public void OptionsSave_AppliesEditedToolbarLayout()
    {
        using var main = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var options = new OptionsDialogViewModel(["English"]);
        options.SetToolbarCustomization(main.ToolbarCommands(), main.ToolbarEntries);
        options.ToolbarLayoutApplied += (_, args) => main.ApplyToolbarLayout(args.Layout);
        var toolbar = Assert.IsType<ToolbarCustomizationViewModel>(options.Toolbar);

        toolbar.SelectedCommand = toolbar.AvailableCommands.Single(
            choice => choice.Descriptor.Id == ToolbarCommandIds.Refresh);
        toolbar.AddCommand.Execute(null);

        options.ConfirmCommand.Execute(null);

        Assert.True(options.DialogAccepted);
        Assert.Contains(main.ToolbarEntries, entry => entry.CommandId == ToolbarCommandIds.Refresh);
    }

    [Fact]
    public void OptionsCancel_LeavesToolbarLayoutUnchanged()
    {
        using var main = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var original = main.ToolbarEntries.Select(entry => entry.CommandId).ToArray();
        var options = new OptionsDialogViewModel(["English"]);
        options.SetToolbarCustomization(main.ToolbarCommands(), main.ToolbarEntries);
        options.ToolbarLayoutApplied += (_, args) => main.ApplyToolbarLayout(args.Layout);
        var toolbar = Assert.IsType<ToolbarCustomizationViewModel>(options.Toolbar);

        toolbar.SelectedCommand = toolbar.AvailableCommands.Single(
            choice => choice.Descriptor.Id == ToolbarCommandIds.Refresh);
        toolbar.AddCommand.Execute(null);

        Assert.False(options.DialogAccepted);
        Assert.Equal(original, main.ToolbarEntries.Select(entry => entry.CommandId).ToArray());
    }

    [Fact]
    public void ToolbarCommands_UseLocalizedLabels()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var find = vm.ToolbarCommands().Single(command => command.Id == ToolbarCommandIds.Find);

        Assert.Equal(LocalizationService.Current.Get(TextKeys.Find), find.Label);
    }

    [Fact]
    public void ToolbarCommands_KeepMnemonicMarkersInDescriptorLabels()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var newCatalog = vm.ToolbarCommands().Single(command => command.Id == ToolbarCommandIds.NewCatalog);

        Assert.Equal(LocalizationService.Current.Get(TextKeys.New), newCatalog.Label);
        Assert.Contains("_", newCatalog.Label);
    }

    [Fact]
    public void CustomizationLists_StripMnemonicMarkersFromLabels()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var descriptor = vm.ToolbarCommands().Single(command => command.Id == ToolbarCommandIds.NewCatalog);
        var dialog = new ToolbarCustomizationViewModel(vm.ToolbarCommands(), ToolbarLayout.CreateDefault());

        var choice = dialog.AvailableCommands.Single(item => item.Descriptor.Id == ToolbarCommandIds.NewCatalog);
        var entry = dialog.SelectedItems.Single(item => item.CommandId == ToolbarCommandIds.NewCatalog);

        Assert.Equal(MnemonicText.Strip(descriptor.Label), choice.Label);
        Assert.Equal(MnemonicText.Strip(descriptor.Label), entry.Label);
        Assert.DoesNotContain("_", choice.Label);
        Assert.DoesNotContain("_", entry.Label);
    }

    [Fact]
    public void CustomizationLists_StripBothMnemonicMarkers()
    {
        var dialog = new ToolbarCustomizationViewModel(
            [CreateDescriptor("&Save _As...")],
            [ToolbarItemDocument.Command("test-command")]);

        Assert.Equal("&Save _As...", Assert.Single(dialog.AvailableCommands).Descriptor.Label);
        Assert.Equal("Save As...", Assert.Single(dialog.AvailableCommands).Label);
        Assert.Equal("Save As...", Assert.Single(dialog.SelectedItems).Label);
    }

    [Fact]
    public void CustomizationLists_KeepSeparatorLabelUnchanged()
    {
        var dialog = new ToolbarCustomizationViewModel(
            [CreateDescriptor("_New")],
            [ToolbarItemDocument.Separator()]);

        var separator = Assert.Single(dialog.SelectedItems);

        Assert.True(separator.IsSeparator);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.Separator), separator.Label);
    }

    private static ToolbarCommandDescriptor CreateDescriptor(string label) => new()
    {
        Id = "test-command",
        Category = "File",
        Label = label,
        Command = new TestCommand()
    };

    private sealed class TestCommand : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }
    }
}
