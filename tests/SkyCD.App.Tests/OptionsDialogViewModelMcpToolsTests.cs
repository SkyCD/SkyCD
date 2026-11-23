using System.Linq;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class OptionsDialogViewModelMcpToolsTests
{
    private static readonly string AiGroup = LocalizationService.Current.Get(UiKeys.AiGroup);

    private static readonly (string Name, string CommandPath)[] DiscoveredTools =
    [
        ("skycd.plugins.list", "plugins list"),
        ("skycd.open", "open"),
        ("skycd.fileformats.list", "fileformats list")
    ];

    [Fact]
    public void SetMcpTools_NullSelection_ExposesEveryToolAndPersistsAsDefault()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SetMcpTools(DiscoveredTools, exposedToolNames: null);

        Assert.Equal(3, vm.McpTools.Count);
        Assert.All(vm.McpTools, tool => Assert.True(tool.IsExposed));
        Assert.Null(vm.GetExposedMcpTools());
    }

    [Fact]
    public void SetMcpTools_ExplicitSelection_ExposesOnlyListedToolsCaseInsensitively()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SetMcpTools(DiscoveredTools, ["SKYCD.PLUGINS.LIST"]);

        Assert.Equal(["skycd.fileformats.list", "skycd.open", "skycd.plugins.list"],
            vm.McpTools.Select(tool => tool.Name));
        Assert.Equal(["skycd.plugins.list"], vm.McpTools.Where(tool => tool.IsExposed).Select(tool => tool.Name));
        Assert.Equal(["skycd.plugins.list"], vm.GetExposedMcpTools());
    }

    [Fact]
    public void SetMcpTools_EmptySelection_ExposesNothing()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SetMcpTools(DiscoveredTools, []);

        Assert.All(vm.McpTools, tool => Assert.False(tool.IsExposed));
        Assert.Empty(vm.GetExposedMcpTools()!);
    }

    [Fact]
    public void GetExposedMcpTools_DiscoveryFailed_PreservesStoredSelection()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.InitializeMcpTools(["skycd.open"]);

        Assert.Equal(["skycd.open"], vm.GetExposedMcpTools());
        Assert.Empty(vm.McpTools);
    }

    [Fact]
    public void SetMcpTools_DropsDuplicatesIgnoringCase()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SetMcpTools([("skycd.open", "open"), ("SKYCD.OPEN", "open again")], null);

        var tool = Assert.Single(vm.McpTools);
        Assert.Equal("open", tool.CommandPath);
    }

    [Fact]
    public void SelectAllAndClear_UpdateSelectionSummaryAndPersistence()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetMcpTools(DiscoveredTools, ["skycd.open"]);
        var summaryChanges = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(OptionsDialogViewModel.McpExposedSummary)) summaryChanges++;
        };

        vm.SelectAllMcpToolsCommand.Execute(null);

        Assert.All(vm.McpTools, tool => Assert.True(tool.IsExposed));
        Assert.Null(vm.GetExposedMcpTools());
        Assert.True(summaryChanges > 0);

        vm.ClearMcpToolsCommand.Execute(null);

        Assert.All(vm.McpTools, tool => Assert.False(tool.IsExposed));
        Assert.Empty(vm.GetExposedMcpTools()!);
    }

    [Fact]
    public void ToggleTool_UpdatesSummaryAndPersistence()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetMcpTools(DiscoveredTools, null);

        vm.McpTools.Single(tool => tool.Name == "skycd.open").IsExposed = false;

        Assert.Equal(LocalizationService.Current.Format(UiKeys.McpExposedCount, 2, 3), vm.McpExposedSummary);
        Assert.Equal(["skycd.fileformats.list", "skycd.plugins.list"], vm.GetExposedMcpTools());
    }

    [Fact]
    public void SearchText_FiltersByNameAndCommandPath()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetMcpTools(DiscoveredTools, null);

        vm.McpToolSearchText = "format";

        var match = Assert.Single(vm.FilteredMcpTools);
        Assert.Equal("skycd.fileformats.list", match.Name);
        Assert.False(vm.ShowNoMatchingMcpTools);

        vm.McpToolSearchText = "missing";

        Assert.Empty(vm.FilteredMcpTools);
        Assert.True(vm.ShowNoMatchingMcpTools);
    }

    [Fact]
    public void Summary_UsesLocalizedCountFormat()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetMcpTools(DiscoveredTools, ["skycd.open"]);

        Assert.Equal(LocalizationService.Current.Format(UiKeys.McpExposedCount, 1, 3), vm.McpExposedSummary);
        Assert.Contains(" of ", vm.McpExposedSummary);
    }

    [Fact]
    public void SettingCategories_AlwaysListsMcpToolsUnderAiGroup()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        var categories = vm.SettingCategories.Where(category => category.BuiltInIndex >= 0).ToList();
        var category = categories.Single(entry => entry.Id == "mcp-tools");

        Assert.Equal("MCP Tools", category.Title);
        Assert.Equal(AiGroup, category.Group);
        Assert.Equal(categories[^1], category);
        Assert.True(category.BuiltInIndex > categories.Single(entry => entry.Id == "toolbar").BuiltInIndex);

        vm.SetAiDescriptionAvailable(true);

        Assert.Equal(category.BuiltInIndex,
            vm.SettingCategories.Single(entry => entry.Id == "mcp-tools").BuiltInIndex);
    }

    [Fact]
    public void SettingsSearch_McpToolsKeywords_SelectsThePage()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SettingsSearchText = "MCP tools";

        Assert.Equal(["mcp-tools"], vm.FilteredSettingCategories.Select(category => category.Id));
        Assert.True(vm.IsMcpToolsCategorySelected);
        Assert.True(vm.ShowMcpToolsSection);
        Assert.True(vm.HasVisibleCategoryContent);
    }

    [Fact]
    public void ShowMcpToolsSection_IsFalseOnOtherPages()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetMcpTools(DiscoveredTools, null);
        vm.SelectedSettingCategory = vm.SettingCategories.Single(category => category.Id == "mcp-tools");

        Assert.True(vm.IsMcpToolsCategorySelected);
        Assert.True(vm.ShowMcpToolsSection);

        vm.SelectedSettingCategory = vm.SettingCategories.Single(category => category.Id == "mcp");

        Assert.False(vm.IsMcpToolsCategorySelected);
        Assert.False(vm.ShowMcpToolsSection);
        Assert.True(vm.ShowMcpSection);
    }

    [Fact]
    public void SetAiDescriptionAvailable_Unavailable_KeepsMcpToolsPageSelected()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetAiDescriptionAvailable(true);
        vm.SelectedSettingCategory = vm.SettingCategories.Single(category => category.Id == "mcp-tools");

        vm.SetAiDescriptionAvailable(false);

        Assert.True(vm.IsMcpToolsCategorySelected);
        Assert.Equal("mcp-tools", vm.SelectedSettingCategory?.Id);
        Assert.DoesNotContain(vm.SettingCategories, category => category.Id == "ai-description");
    }
}
