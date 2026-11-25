using System.Globalization;
using System.Linq;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public class OptionsDialogUiGroupTests
{
    private static readonly string UiGroup = LocalizationService.Current.Get(UiKeys.UiGroup);
    private static readonly string DetailsColumns = LocalizationService.Current.Get(UiKeys.DetailsColumns);

    [Theory]
    [InlineData("en", "User Interface (UI)", "Artificial Intelligence (AI)", "MCP Server", "MCP Tools",
        "MCP server settings")]
    [InlineData("lt", "Vartotojo sąsaja (UI)", "Dirbtinis intelektas (AI)", "MCP serveris", "MCP įrankiai",
        "MCP serverio nustatymai")]
    public void OptionsLabels_AreExpandedInEveryShippedLanguage(string cultureCode, string uiGroup,
        string aiGroup, string mcpPage, string mcpTools, string mcpSettingsHeading)
    {
        var service = new LocalizationService();
        var culture = CultureInfo.GetCultureInfo(cultureCode);

        Assert.Equal(uiGroup, service.Get(UiKeys.UiGroup, culture));
        Assert.Equal(aiGroup, service.Get(UiKeys.AiGroup, culture));
        Assert.Equal(mcpPage, service.Get(TextKeys.MCP, culture));
        Assert.Equal(mcpTools, service.Get(UiKeys.McpTools, culture));
        Assert.Equal(mcpSettingsHeading, service.Get(UiKeys.McpServerSettings, culture));
    }

    [Fact]
    public void UiPages_RenderUnderSingleGroupHeader()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        var header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader && entry.Title == UiGroup);
        Assert.Equal(UiGroup, header.Title);
        var headerIndex = vm.SidebarEntries.IndexOf(header);
        var children = vm.SidebarEntries.Skip(headerIndex + 1)
            .TakeWhile(entry => entry.Depth > 0)
            .ToList();
        Assert.Equal(["details-columns", "toolbar"], children.Select(child => child.Category?.Id));
        Assert.All(children, child => Assert.Equal(1, child.Depth));
    }

    [Fact]
    public void SelectingUiHeader_SelectsDetailsColumnsAndShowsFullPath()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader && entry.Title == UiGroup);

        vm.SelectedSidebarEntry = header;

        Assert.Equal("details-columns", vm.SelectedSettingCategory?.Id);
        Assert.False(vm.SelectedSidebarEntry!.IsGroupHeader);
        Assert.Equal($"{UiGroup}  >  {DetailsColumns}", vm.CurrentCategoryPath);
        Assert.True(vm.ShowDetailsColumnsSection);
    }

    [Fact]
    public void SelectingToolbarChild_ShowsToolbarSection()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var toolbar = vm.SidebarEntries.Single(entry => entry.Category?.Id == "toolbar");

        vm.SelectedSidebarEntry = toolbar;

        Assert.True(vm.IsToolbarCategorySelected);
        Assert.True(vm.ShowToolbarSection);
        Assert.Equal($"{UiGroup}  >  {LocalizationService.Current.Get(UiKeys.Toolbar)}", vm.CurrentCategoryPath);
    }

    [Fact]
    public void RestoreSettingCategory_ResolvesGroupedToolbarById()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.RestoreSettingCategory("toolbar", 0);

        Assert.Equal("toolbar", vm.SelectedSettingCategory?.Id);
        Assert.Equal("toolbar", vm.SelectedSidebarEntry?.Category?.Id);
    }

    [Theory]
    [InlineData("toolbar", "toolbar")]
    [InlineData("columns", "details-columns")]
    public void Search_KeepsUiHeaderWithMatchingChild(string searchText, string expectedId)
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SettingsSearchText = searchText;

        var header = Assert.Single(vm.SidebarEntries, entry => entry.IsGroupHeader);
        Assert.Equal(UiGroup, header.Title);
        var child = Assert.Single(vm.SidebarEntries, entry => !entry.IsGroupHeader);
        Assert.Equal(expectedId, child.Category?.Id);
        Assert.Equal(expectedId, vm.SelectedSettingCategory?.Id);
    }

    [Fact]
    public void Search_RemovesUiHeaderWhenNoChildMatches()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SettingsSearchText = "language";

        Assert.DoesNotContain(vm.SidebarEntries, entry => entry.IsGroupHeader);
        Assert.DoesNotContain(vm.FilteredSettingCategories, category => category.Group == UiGroup);
    }
}
