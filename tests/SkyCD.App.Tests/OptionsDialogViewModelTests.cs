using SkyCD.Presentation.ViewModels;

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
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "skycd.plugin.json v2.0.0", supportsConfiguration: true),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "skycd.plugin.xml v2.0.0", supportsConfiguration: true)
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
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "skycd.plugin.json v2.0.0", supportsConfiguration: false),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "skycd.plugin.xml v2.0.0", supportsConfiguration: false)
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

        vm.SelectedTabIndex = 1;

        Assert.Equal(1, vm.SelectedTabIndex);
    }

    [Fact]
    public void SelectedTabIndex_ClampsOutOfRangeValues()
    {
        var vm = new OptionsDialogViewModel(["English"]);

        vm.SelectedTabIndex = -5;
        Assert.Equal(0, vm.SelectedTabIndex);

        vm.SelectedTabIndex = 99;
        Assert.Equal(1, vm.SelectedTabIndex);
    }

    [Fact]
    public void SearchText_FiltersPluginSections()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SelectedTabIndex = 0;

        vm.SettingsSearchText = "configure";

        Assert.False(vm.ShowPluginPathSection);
        Assert.False(vm.ShowPluginListSection);
        Assert.True(vm.ShowPluginActionsSection);
        Assert.False(vm.ShowPluginInfoSection);
        Assert.True(vm.HasVisibleCategoryContent);
        Assert.False(vm.ShowNoSearchResults);
    }

    [Fact]
    public void SearchText_ShowsNoResultsWhenCategoryHasNoMatch()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SelectedTabIndex = 1;

        vm.SettingsSearchText = "plugin";

        Assert.False(vm.ShowLanguageSection);
        Assert.False(vm.HasVisibleCategoryContent);
        Assert.True(vm.ShowNoSearchResults);
    }

    [Fact]
    public void SearchText_FiltersPluginsByContent()
    {
        var vm = new OptionsDialogViewModel(["English"]);
        vm.SelectedTabIndex = 0;
        vm.SetPlugins(
        [
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "json v2.0.0", id: "plugin.json"),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "xml v2.0.0", id: "plugin.xml")
        ]);

        vm.SettingsSearchText = "xml";

        Assert.Single(vm.FilteredPlugins);
        Assert.Equal("XML", vm.FilteredPlugins[0].Name);
        Assert.True(vm.ShowPluginListSection);
    }

    [Fact]
    public void SearchText_FiltersLanguagesByContent()
    {
        var vm = new OptionsDialogViewModel(["English", "Lithuanian"]);
        vm.SelectedTabIndex = 1;

        vm.SettingsSearchText = "lith";

        Assert.Single(vm.FilteredLanguages);
        Assert.Equal("Lithuanian", vm.FilteredLanguages[0].Name);
        Assert.True(vm.ShowLanguageSection);
        Assert.False(vm.ShowNoSearchResults);
    }
}
