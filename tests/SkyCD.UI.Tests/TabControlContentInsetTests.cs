using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.App.Views;
using SkyCD.Documents.Collections;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #1000: the shared card TabControl theme insets the content of every dialog tab surface
/// (About, Options and Properties) instead of each window padding its own tab bodies.
/// </summary>
public sealed class TabControlContentInsetTests
{
    [AvaloniaFact]
    public void About_TabContentSitsInsideTheStripAndBorder()
    {
        var window = Show(new AboutWindow { DataContext = new AboutDialogViewModel() });
        try
        {
            var tabs = Assert.IsType<TabControl>(window.FindControl<Control>("MainTabs"));
            AssertSharedInset(tabs);

            tabs.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            var body = Assert.IsAssignableFrom<Control>(Assert.IsType<TabItem>(tabs.Items[1]).Content);
            AssertInsetFromContentHost(tabs, body);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Properties_EveryTabBodySharesTheInset()
    {
        var viewModel = new PropertiesDialogViewModel("key", "name", "folder", string.Empty, new PropertiesCollection());
        var window = Show(new PropertiesWindow { DataContext = viewModel });
        try
        {
            var tabs = Assert.Single(window.GetVisualDescendants().OfType<TabControl>());
            AssertSharedInset(tabs);

            for (var index = 0; index < tabs.Items.Count; index++)
            {
                var item = Assert.IsType<TabItem>(tabs.Items[index]);
                if (!item.IsVisible)
                {
                    continue;
                }

                tabs.SelectedIndex = index;
                Dispatcher.UIThread.RunJobs();
                AssertInsetFromContentHost(tabs, Assert.IsAssignableFrom<Control>(item.Content));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Options_InstalledAndMarketplaceTabsShareTheInset()
    {
        var viewModel = new OptionsDialogViewModel(["English"]);
        viewModel.SetPlugins(
        [
            new OptionsPluginItem("JSON", "SkyCD", "json v1", id: "json", primaryExtension: "json")
        ]);
        var window = Show(new OptionsWindow { DataContext = viewModel });
        try
        {
            var tabs = window.GetVisualDescendants().OfType<TabControl>().ToArray();

            var installed = Assert.Single(tabs, tab => tab.ItemCount == 3);
            AssertSharedInset(installed);
            AssertInsetFromContentHost(installed,
                Assert.IsAssignableFrom<Control>(Assert.IsType<TabItem>(installed.SelectedItem).Content));

            viewModel.IsMarketplaceBrowseSelected = true;
            Dispatcher.UIThread.RunJobs();

            var marketplace = Assert.Single(tabs, tab => tab.ItemCount == 2);
            AssertSharedInset(marketplace);
            AssertInsetFromContentHost(marketplace,
                Assert.IsAssignableFrom<Control>(Assert.IsType<TabItem>(marketplace.SelectedItem).Content));
        }
        finally
        {
            window.Close();
        }
    }

    private static Window Show(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void AssertSharedInset(TabControl tabs)
    {
        var inset = SharedInset();
        Assert.Equal(new Thickness(12), inset);
        Assert.Equal(inset, tabs.Padding);
    }

    private static void AssertInsetFromContentHost(TabControl tabs, Control body)
    {
        var inset = SharedInset();
        var host = Assert.Single(tabs.GetVisualDescendants().OfType<ContentPresenter>(),
            presenter => presenter.Name == "PART_SelectedContentHost" && presenter.IsEffectivelyVisible);

        Assert.Equal(inset, host.Margin);
        Assert.IsType<Panel>(host.Parent);

        var offset = body.TranslatePoint(new Point(0, 0), (Visual)host.Parent!)!.Value;

        Assert.True(offset.X >= inset.Left, $"Tab content starts at x={offset.X}; expected at least {inset.Left}.");
        Assert.True(offset.Y >= inset.Top, $"Tab content starts at y={offset.Y}; expected at least {inset.Top}.");
    }

    private static Thickness SharedInset()
    {
        Assert.True(Application.Current!.TryGetResource("SkyInsetMd", null, out var value));
        return Assert.IsType<Thickness>(value);
    }
}
