using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Documents.Collections;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Covers the plugin-contributed Properties tabs: they render after the built-ins, a requested tab is
/// selected on open, and the dialog disposes plugin content when it closes.
/// </summary>
public class PropertiesWindowPluginTabsTests
{
    [AvaloniaFact]
    public void PluginTab_IsRenderedAfterBuiltInTabs()
    {
        var viewModel = CreateViewModel();
        viewModel.InitializeDetailsTabs([DetailsTab()]);
        var window = Show(viewModel);
        try
        {
            var tabs = FindTabs(window).Items.OfType<TabItem>().ToArray();

            Assert.Equal("Demo", tabs[^1].Header);
            Assert.Contains(tabs, tab => (string?)tab.Header == "General");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InitialDetailsTabKey_SelectsThePluginTab()
    {
        var viewModel = CreateViewModel();
        viewModel.InitialDetailsTabKey = "demo.details";
        viewModel.InitializeDetailsTabs([DetailsTab()]);
        var window = Show(viewModel);
        try
        {
            var selected = Assert.IsType<TabItem>(FindTabs(window).SelectedItem);

            Assert.Equal("Demo", selected.Header);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NoContributions_RendersNoPluginTab()
    {
        var viewModel = CreateViewModel();
        var window = Show(viewModel);
        try
        {
            Assert.DoesNotContain(FindTabs(window).Items.OfType<TabItem>(),
                tab => (string?)tab.Header == "Demo");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ClosingTheDialog_DisposesPluginTabs()
    {
        var disposed = false;
        var viewModel = CreateViewModel();
        viewModel.InitializeDetailsTabs(
        [
            new EntryDetailsTabContribution(
                "demo.details",
                "Demo",
                0,
                () => new EntryDetailsTab(() => new TextBlock { Text = "demo" }, () => disposed = true))
        ]);
        var window = Show(viewModel);

        window.Close();

        Assert.True(disposed);
    }

    private static EntryDetailsTabContribution DetailsTab() =>
        new("demo.details", "Demo", 0, () => new EntryDetailsTab(() => new TextBlock { Text = "demo" }));

    private static PropertiesDialogViewModel CreateViewModel() =>
        new("key", "name", "icon", string.Empty, new PropertiesCollection());

    private static PropertiesWindow Show(PropertiesDialogViewModel viewModel)
    {
        var window = new PropertiesWindow { DataContext = viewModel, Width = 411, Height = 560 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static TabControl FindTabs(PropertiesWindow window) =>
        Assert.Single(window.GetVisualDescendants().OfType<TabControl>());
}
