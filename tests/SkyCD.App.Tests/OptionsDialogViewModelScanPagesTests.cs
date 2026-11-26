using System;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class OptionsDialogViewModelScanPagesTests
{
    [Fact]
    public void ScanGroup_ExposesThreeContiguousChildren()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var categories = vm.SettingCategories.ToList();
        var scan = categories.FindIndex(category => category.Id == OptionsDialogViewModel.ScanCategoryId);
        var children = categories.Skip(scan).Take(3).ToArray();

        Assert.Equal(
            [
                OptionsDialogViewModel.ScanCategoryId,
                OptionsDialogViewModel.ScanRulesCategoryId,
                OptionsDialogViewModel.ScanCompletionCategoryId
            ],
            children.Select(category => category.Id));
        Assert.Equal(
            [
                OptionsDialogViewModel.ScanCategoryIndex,
                OptionsDialogViewModel.ScanRulesCategoryIndex,
                OptionsDialogViewModel.ScanCompletionCategoryIndex
            ],
            children.Select(category => category.BuiltInIndex));
        Assert.All(children, category => Assert.Equal(
            LocalizationService.Current.Get(UiKeys.ScanExclusions), category.Group));
        Assert.Equal(LocalizationService.Current.Get(UiKeys.ScanSettings), children[0].Title);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.ExclusionByScanName), children[1].Title);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.ScanCompletionPage), children[2].Title);

        var header = Assert.Single(vm.SidebarEntries, entry =>
            entry.IsGroupHeader && entry.Title == LocalizationService.Current.Get(UiKeys.ScanExclusions));
        var sidebarChildren = vm.SidebarEntries.Skip(vm.SidebarEntries.IndexOf(header) + 1)
            .TakeWhile(entry => entry.Depth > 0)
            .ToList();
        Assert.Equal(children.Select(category => category.Id), sidebarChildren.Select(entry => entry.Category?.Id));
        Assert.All(sidebarChildren, child => Assert.Equal(1, child.Depth));
    }

    [Fact]
    public void SelectingScanHeader_OpensScanSettingsWithoutDoublingTheGroupName()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var group = LocalizationService.Current.Get(UiKeys.ScanExclusions);
        var header = vm.SidebarEntries.Single(entry => entry.IsGroupHeader && entry.Title == group);

        vm.SelectedSidebarEntry = header;

        Assert.Equal(OptionsDialogViewModel.ScanCategoryId, vm.SelectedSettingCategory?.Id);
        Assert.Equal($"{group}  >  {LocalizationService.Current.Get(UiKeys.ScanSettings)}", vm.CurrentCategoryPath);
        Assert.NotEqual($"{group}  >  {group}", vm.CurrentCategoryPath);
        Assert.True(vm.ShowScanSection);
        Assert.False(vm.ShowScanRulesSection);
        Assert.False(vm.ShowScanCompletionSection);
    }

    [Theory]
    [InlineData(OptionsDialogViewModel.ScanCategoryId, true, false, false)]
    [InlineData(OptionsDialogViewModel.ScanRulesCategoryId, false, true, false)]
    [InlineData(OptionsDialogViewModel.ScanCompletionCategoryId, false, false, true)]
    public void EachScanPage_ShowsOnlyItsSection(string id, bool settings, bool rules, bool completion)
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SelectedSettingCategory = vm.SettingCategories.Single(category => category.Id == id);

        Assert.Equal(settings, vm.ShowScanSection);
        Assert.Equal(rules, vm.ShowScanRulesSection);
        Assert.Equal(completion, vm.ShowScanCompletionSection);
        Assert.True(vm.HasVisibleCategoryContent);
    }

    [Fact]
    public void Search_HidesEveryScanSection()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SelectedSettingCategory = vm.SettingCategories.Single(category =>
            category.Id == OptionsDialogViewModel.ScanCategoryId);

        vm.SettingsSearchText = "zzzz-no-page";

        Assert.False(vm.ShowScanSection);
        Assert.False(vm.ShowScanRulesSection);
        Assert.False(vm.ShowScanCompletionSection);
        Assert.False(vm.HasVisibleCategoryContent);
    }

    [Fact]
    public void Search_ForTheGroupName_KeepsAllThreePages()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SettingsSearchText = LocalizationService.Current.Get(UiKeys.ScanExclusions);

        Assert.Equal(
            [
                OptionsDialogViewModel.ScanCategoryId,
                OptionsDialogViewModel.ScanRulesCategoryId,
                OptionsDialogViewModel.ScanCompletionCategoryId
            ],
            vm.FilteredSettingCategories.Select(category => category.Id));
        var header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader);
        Assert.Equal(LocalizationService.Current.Get(UiKeys.ScanExclusions), header.Title);
    }

    [Fact]
    public void OptionsAndRules_RoundTripFromEveryScanPage()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetScanOptions(new ScanSettingsSnapshot(
            SkipSymbolicLinks: true,
            HiddenSystemFilePolicy: "Include",
            SkipRecycleBins: false,
            SkipWellKnownSystemFolders: false,
            SkipEmptyFolders: true,
            SkipOnlineOnlyCloudFiles: true,
            IncludeExtendedInfo: true,
            ShowScanErrorsAfterScan: false,
            SaveScanErrorsToComments: true,
            EjectMediaAfterScan: true,
            ActivateMainWindowAfterScan: true,
            PlaySoundAfterScan: true));
        vm.SetScanExclusionRules(
        [
            new ScanExclusionRuleDocument { Pattern = "*.tmp", EntryType = "File", IsInclude = false }
        ]);

        foreach (var id in new[]
                 {
                     OptionsDialogViewModel.ScanCategoryId,
                     OptionsDialogViewModel.ScanRulesCategoryId,
                     OptionsDialogViewModel.ScanCompletionCategoryId
                 })
        {
            vm.SelectedSettingCategory = vm.SettingCategories.Single(category => category.Id == id);
            var options = vm.GetScanOptions();
            Assert.True(options.SkipSymbolicLinks);
            Assert.True(options.SaveScanErrorsToComments);
            Assert.True(options.EjectMediaAfterScan);
            Assert.True(options.ActivateMainWindowAfterScan);
            Assert.True(options.PlaySoundAfterScan);
            Assert.Equal("*.tmp", Assert.Single(vm.GetScanExclusionRules()).Pattern);
        }
    }

    [Fact]
    public async Task BlankRule_OnSave_RevealsTheRulesPage()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SelectedSettingCategory = vm.SettingCategories.Single(category =>
            category.Id == OptionsDialogViewModel.ScanCategoryId);
        vm.SettingsSearchText = LocalizationService.Current.Get(UiKeys.ScanSettings);
        vm.AddScanExclusionRuleCommand.Execute(null);
        vm.SelectedScanExclusionRule!.Pattern = "   ";

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.True(vm.ShowScanAlert);
        Assert.Equal(OptionsDialogViewModel.ScanRulesCategoryId, vm.SelectedSettingCategory?.Id);
        Assert.True(vm.ShowScanRulesSection);
        Assert.False(vm.DialogAccepted);
    }
}
