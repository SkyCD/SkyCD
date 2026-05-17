using System.Linq;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class OptionsDialogViewModelTests
{
    [Fact]
    public void Constructor_InitializesLanguageSelection()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);

        Assert.Equal(2, vm.Languages.Count);
        Assert.Equal("English", vm.SelectedLanguage?.Name);
    }

    [Fact]
    public void RefreshPluginsCommand_RaisesRefreshRequest()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var raised = false;
        vm.RefreshPluginsRequested += (_, _) => raised = true;

        vm.RefreshPluginsCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void SetPlugins_SelectsFirstPluginAndEnablesConfigure()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var plugins = new[]
        {
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "skycd.plugin.json v2.0.0",
                supportsConfiguration: true),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "skycd.plugin.xml v2.0.0",
                supportsConfiguration: true)
        };

        vm.SetPlugins(plugins);

        Assert.Equal(2, vm.Plugins.Count);
        Assert.Equal("JSON", vm.SelectedPlugin?.Name);
        Assert.True(vm.ConfigurePluginCommand.CanExecute(null));
    }

    [Fact]
    public void SetPlugins_DisablesConfigureWhenPluginDoesntSupportConfiguration()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var plugins = new[]
        {
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "skycd.plugin.json v2.0.0",
                supportsConfiguration: false),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "skycd.plugin.xml v2.0.0",
                supportsConfiguration: false)
        };

        vm.SetPlugins(plugins);

        Assert.Equal(2, vm.Plugins.Count);
        Assert.Equal("JSON", vm.SelectedPlugin?.Name);
        Assert.False(vm.ConfigurePluginCommand.CanExecute(null));
    }

    [Fact]
    public void SetPlugins_RespectsPreviouslyDisabledPluginIds()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetDisabledPluginIds(["plugin.xml"]);

        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);

        Assert.True(vm.Plugins.Single(plugin => plugin.Id == "plugin.json").IsEnabled);
        Assert.False(vm.Plugins.Single(plugin => plugin.Id == "plugin.xml").IsEnabled);
    }

    [Fact]
    public void GetDisabledPluginIds_ReturnsUncheckedPluginIds()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);

        vm.Plugins.Single(plugin => plugin.Id == "plugin.xml").IsEnabled = false;

        var disabled = vm.GetDisabledPluginIds();

        Assert.Equal(["plugin.xml"], disabled);
    }

    [Fact]
    public void SelectedTabIndex_CanBeUpdated()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SelectedTabIndex = 2;

        Assert.Equal(2, vm.SelectedTabIndex);
    }

    [Fact]
    public void SelectedTabIndex_ClampsOutOfRangeValues()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SelectedTabIndex = -5;
        Assert.Equal(0, vm.SelectedTabIndex);

        vm.SelectedTabIndex = 99;
        Assert.Equal(3, vm.SelectedTabIndex);
    }

    [Fact]
    public void SearchText_FiltersPluginSections()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SelectedTabIndex = 0;

        vm.SettingsSearchText = "plug";

        Assert.Equal(["Plugins"], vm.FilteredSettingCategories);
        Assert.True(vm.ShowPluginPathSection);
        Assert.True(vm.ShowPluginListSection);
        Assert.True(vm.ShowPluginActionsSection);
        Assert.True(vm.ShowPluginInfoSection);
        Assert.True(vm.HasVisibleCategoryContent);
        Assert.False(vm.ShowNoSearchResults);
    }

    [Fact]
    public void SearchText_ShowsNoResultsWhenCategoryHasNoMatch()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SelectedTabIndex = 0;

        vm.SettingsSearchText = "terminal";

        Assert.Empty(vm.FilteredSettingCategories);
        Assert.False(vm.ShowPluginPathSection);
        Assert.False(vm.HasVisibleCategoryContent);
        Assert.True(vm.ShowNoSearchResults);
    }

    [Fact]
    public void SearchText_FiltersLeftCategoryList()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);

        vm.SettingsSearchText = "lang";

        Assert.Equal(["Language"], vm.FilteredSettingCategories);
        Assert.Equal("Language", vm.SelectedSettingCategory);
        Assert.Equal(1, vm.SelectedTabIndex);
        Assert.True(vm.ShowLanguageSection);
    }

    [Fact]
    public void SearchText_FiltersMcpCategory()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);

        vm.SettingsSearchText = "mcp";

        Assert.Equal(["MCP"], vm.FilteredSettingCategories);
        Assert.Equal("MCP", vm.SelectedSettingCategory);
        Assert.Equal(2, vm.SelectedTabIndex);
        Assert.True(vm.ShowMcpSection);
    }

    [Fact]
    public void SearchText_FiltersStatusesCategory()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);

        vm.SettingsSearchText = "status";

        Assert.Equal(["Statuses"], vm.FilteredSettingCategories);
        Assert.Equal("Statuses", vm.SelectedSettingCategory);
        Assert.Equal(3, vm.SelectedTabIndex);
        Assert.True(vm.ShowStatusesSection);
    }

    [Fact]
    public void SearchText_DoesNotFilterRightPanelItemCollections()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);
        vm.SelectedTabIndex = 0;

        vm.SettingsSearchText = "plug";

        Assert.Equal(2, vm.Plugins.Count);
        Assert.Equal(2, vm.Languages.Count);
    }

    [Fact]
    public void McpPort_IsClampedAndBuildsUrl()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.McpPort = 0;
        Assert.Equal(1, vm.McpPort);

        vm.McpPort = 70000;
        Assert.Equal(65535, vm.McpPort);

        vm.McpPort = 8787;
        Assert.Equal("http://127.0.0.1:8787/mcp", vm.McpBaseUrl);
    }

    [Fact]
    public void McpServer_CanBeDisabled()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.IsMcpServerEnabled = false;

        Assert.False(vm.IsMcpServerEnabled);
    }

    [Fact]
    public void McpStatusIconVisibility_DefaultsToTrueAndCanBeDisabled()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        Assert.True(vm.IsMcpStatusIconVisible);
        Assert.Equal("Copy URL", vm.McpCopyTooltip);
        Assert.False(vm.ShowMcpAlert);
        vm.IsMcpStatusIconVisible = false;
        Assert.False(vm.IsMcpStatusIconVisible);
    }

    [Fact]
    public void StatusVariants_CanBeAddedRemovedAndExported()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetStatusVariants(
        [
            new StatusVariantDocument
            {
                Name = "Watched",
                IconGlyph = "check",
                ItemTypes = [SkyCD.Documents.Enum.CatalogDocumentType.Media]
            }
        ]);

        Assert.Single(vm.StatusVariants);
        Assert.True(vm.RemoveStatusVariantCommand.CanExecute(null));

        vm.AddStatusVariantCommand.Execute(null);
        Assert.Equal(2, vm.StatusVariants.Count);

        vm.SelectedStatusVariant = vm.StatusVariants[0];
        vm.RemoveStatusVariantCommand.Execute(null);
        Assert.Single(vm.StatusVariants);

        var exported = vm.GetStatusVariants();
        Assert.Single(exported);
        Assert.False(string.IsNullOrWhiteSpace(exported[0].Name));
        Assert.Contains(SkyCD.Documents.Enum.CatalogDocumentType.Media, exported[0].ItemTypes!);
    }

    [Fact]
    public void ConfirmCommand_ShowsStatusAlert_WhenAnyStatusIconIsMissing()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.AddStatusVariantCommand.Execute(null);

        vm.ConfirmCommand.Execute(null);

        Assert.False(vm.DialogAccepted);
        Assert.True(vm.ShowStatusAlert);
        Assert.Equal("All status items must have an icon selected.", vm.StatusAlertMessage);
    }

    [Fact]
    public void ConfirmCommand_Accepts_WhenAllStatusIconsAreSet()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SetStatusVariants(
        [
            new StatusVariantDocument { Name = "Watched", IconGlyph = "check" }
        ]);

        vm.ConfirmCommand.Execute(null);

        Assert.True(vm.DialogAccepted);
        Assert.False(vm.ShowStatusAlert);
    }

    [Fact]
    public void ResetStatusVariantsCommand_RaisesRequestedEvent()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        var raised = false;
        vm.ResetStatusVariantsRequested += (_, _) => raised = true;

        vm.ResetStatusVariantsCommand.Execute(null);

        Assert.True(raised);
    }

    [Fact]
    public void StatusVariantItemType_AllowsMultipleSelections()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.AddStatusVariantCommand.Execute(null);

        var status = vm.StatusVariants.Single();
        status.SetTypeSelected(SkyCD.Documents.Enum.CatalogDocumentType.Media, true);
        status.SetTypeSelected(SkyCD.Documents.Enum.CatalogDocumentType.File, true);

        var exported = vm.GetStatusVariants().Single();
        Assert.Contains(SkyCD.Documents.Enum.CatalogDocumentType.Media, exported.ItemTypes!);
        Assert.Contains(SkyCD.Documents.Enum.CatalogDocumentType.File, exported.ItemTypes!);
    }
}
