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
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "skycd.plugin.sample.json v2.0.0", SupportsConfiguration: true),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "skycd.plugin.sample.xml v2.0.0", SupportsConfiguration: true)
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
            new OptionsPluginItem("JSON", "IFileFormatPluginCapability", "skycd.plugin.sample.json v2.0.0", SupportsConfiguration: false),
            new OptionsPluginItem("XML", "IFileFormatPluginCapability", "skycd.plugin.sample.xml v2.0.0", SupportsConfiguration: false)
        };

        vm.SetPlugins(plugins);

        Assert.Equal(2, vm.Plugins.Count);
        Assert.Equal("JSON", vm.SelectedPlugin?.Name);
        Assert.False(vm.ConfigurePluginCommand.CanExecute(null));
    }
}
