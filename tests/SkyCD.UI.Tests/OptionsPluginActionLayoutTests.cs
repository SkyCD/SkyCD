using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Plugin.Runtime.Marketplace.Models;
using SkyCD.Presentation.Marketplace;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Actions;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// The plugin pages render one split control instead of the old state ComboBox: the primary part carries the
/// current state action and the dropdown carries Uninstall only when the catalog owns the installed files.
/// </summary>
public sealed class OptionsPluginActionLayoutTests
{
    private const string PluginId = "skycd.plugin.demo";

    [AvaloniaFact]
    public void InstalledPluginPane_ShowsSplitButtonWithUninstallDropdown()
    {
        using var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPluginUninstaller(new NeverCalledUninstaller());
        viewModel.SetPlugins([new OptionsPluginItem("Demo", "IFileFormatPluginCapability", "demo v1.0.0", id: PluginId)]);
        Dispatcher.UIThread.RunJobs();

        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var split = window.GetVisualDescendants().OfType<SplitActionButton>()
                .Single(control => control.Name == "InstalledPluginActionButton");
            var primary = split.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "PrimaryActionButton");
            var secondary = split.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "SecondaryActionButton");

            Assert.True(primary.Bounds.Width > 0);
            Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginDisableAction),
                Assert.IsType<string>(primary.Content));
            Assert.True(secondary.IsVisible);

            // The dropdown opens a menu whose single entry is the uninstall action.
            secondary.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            var menu = Assert.IsType<MenuFlyout>(secondary.Flyout);
            var item = Assert.IsType<MenuItem>(Assert.Single(menu.Items));
            Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginUninstallAction), item.Header);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NotInstalledMarketplacePlugin_ShowsInstallWithoutDropdown()
    {
        using var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.IsMarketplaceBrowseSelected = true;
        viewModel.SelectedMarketplacePlugin = new MarketplacePluginItem(Package(PluginId, "1.0.0"));
        Dispatcher.UIThread.RunJobs();

        var window = new OptionsWindow { DataContext = viewModel };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        try
        {
            var split = window.GetVisualDescendants().OfType<SplitActionButton>()
                .Single(control => control.Name == "MarketplacePluginActionButton");
            var primary = split.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "PrimaryActionButton");
            var secondary = split.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "SecondaryActionButton");

            Assert.Equal(LocalizationService.Current.Get(TextKeys.PluginInstallAction),
                Assert.IsType<string>(primary.Content));
            Assert.True(primary.IsEnabled);
            Assert.False(secondary.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static MarketplacePackage Package(string packageId, string version)
    {
        var sourceUri = new Uri("https://publisher.example/plugins.json");
        var release = new MarketplaceRelease
        {
            SourceUri = sourceUri,
            FeedUri = new Uri("https://feed.example/index.json"),
            PackageId = packageId,
            Version = PluginPackageVersion.Parse(version),
            MinHostVersion = new Version(3, 0, 0),
            Compatibility = PluginCompatibilityReport.Eligible()
        };
        return new MarketplacePackage
        {
            SourceUri = sourceUri,
            PackageId = packageId,
            DisplayName = "Demo",
            Publisher = "Acme Publishing",
            Summary = "Test package.",
            Releases = [release],
            BestRelease = release
        };
    }

    private sealed class NeverCalledUninstaller : SkyCD.Plugin.Runtime.Installation.IPluginPackageUninstaller
    {
        public System.Threading.Tasks.Task<SkyCD.Plugin.Runtime.Installation.PluginInstallResult> UninstallAsync(
            string pluginId, System.Threading.CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Layout tests never uninstall.");
    }
}
