using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #997: the plugin title, the tab strip and the inset tab content of both detail panes share the
/// same left edge; the card content inset from #1000 lines the body up with the tab header text.
/// </summary>
public sealed class OptionsPluginDetailAlignmentTests
{
    [AvaloniaFact]
    public void InstalledDetail_HeaderSitsOnTheCardEdgeAndTabTextAlignsWithTheContent()
    {
        var viewModel = CreateViewModel();
        var window = Show(new OptionsWindow { DataContext = viewModel });
        try
        {
            var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>(), tab => tab.ItemCount == 3);
            AssertTitleOnCardEdge(window, tabs, "SelectedPluginTitle");
            AssertHeaderAlignsWithContent(tabs);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MarketplaceDetail_UsesTheSameEdgesAsTheInstalledPane()
    {
        var viewModel = CreateViewModel();
        var window = Show(new OptionsWindow { DataContext = viewModel });
        try
        {
            viewModel.IsMarketplaceBrowseSelected = true;
            Dispatcher.UIThread.RunJobs();

            var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>(), tab => tab.ItemCount == 2);
            AssertTitleOnCardEdge(window, tabs, "SelectedMarketplacePluginTitle");
            AssertHeaderAlignsWithContent(tabs);
        }
        finally
        {
            window.Close();
        }
    }

    private static OptionsDialogViewModel CreateViewModel()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins(
        [
            new OptionsPluginItem("JSON", "SkyCD", "json v1", id: "json", primaryExtension: "json",
                description: "Reads JSON catalogs")
        ]);
        return viewModel;
    }

    private static void AssertTitleOnCardEdge(Window window, TabControl tabs, string titleName)
    {
        var title = Assert.IsType<TextBlock>(window.FindControl<TextBlock>(titleName));
        var titleX = title.TranslatePoint(new Point(0, 0), tabs)!.Value.X;

        Assert.InRange(Math.Abs(titleX), 0, 0.5);
    }

    private static void AssertHeaderAlignsWithContent(TabControl tabs)
    {
        var firstHeader = Assert.IsType<TabItem>(tabs.Items[0]);
        var headerText = Assert.Single(firstHeader.GetVisualDescendants().OfType<TextBlock>());
        var headerX = headerText.TranslatePoint(new Point(0, 0), tabs)!.Value.X;

        var body = Assert.IsAssignableFrom<Control>(Assert.IsType<TabItem>(tabs.SelectedItem).Content);
        var bodyX = body.TranslatePoint(new Point(0, 0), tabs)!.Value.X;

        Assert.InRange(bodyX, headerX - 2, headerX + 2);
    }

    private static Window Show(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }
}
