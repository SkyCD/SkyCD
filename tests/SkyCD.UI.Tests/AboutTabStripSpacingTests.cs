using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using SkyCD.App.Views;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.UI.Tests;

/// <summary>
/// Issue #1058: the About tab strip keeps a gap below the product row and visibly separated headers.
/// </summary>
public sealed class AboutTabStripSpacingTests
{
    [AvaloniaFact]
    public void About_TabStripIsSeparatedFromTheProductRow()
    {
        var window = Show(new AboutWindow { DataContext = new AboutDialogViewModel() });
        try
        {
            var productName = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("ProductNameText"));
            var tabs = Assert.IsType<TabControl>(window.FindControl<Control>("MainTabs"));

            var productBottom = productName.TranslatePoint(new Point(0, productName.Bounds.Height), window)!.Value.Y;
            var tabsTop = tabs.TranslatePoint(new Point(0, 0), window)!.Value.Y;

            Assert.True(tabsTop - productBottom >= 8,
                $"Tab strip starts {tabsTop - productBottom}px below the product row; expected at least 8px.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void About_AdjacentTabHeadersAreSeparated()
    {
        var window = Show(new AboutWindow { DataContext = new AboutDialogViewModel() });
        try
        {
            var tabs = Assert.IsType<TabControl>(window.FindControl<Control>("MainTabs"));
            var strip = (Panel)tabs.ItemsPanelRoot!;

            var rows = strip.Children
                .OfType<TabItem>()
                .GroupBy(header => header.Bounds.Y)
                .Select(row => row.OrderBy(header => header.Bounds.X).ToArray())
                .ToArray();

            var renderedHeaders = rows.Sum(row => row.Length);

            Assert.True(renderedHeaders >= 2, "The About window should render at least two tab headers.");

            foreach (var row in rows.Where(row => row.Length > 1))
            {
                for (var index = 1; index < row.Length; index++)
                {
                    var previous = row[index - 1].Bounds;
                    var gap = row[index].Bounds.X - (previous.X + previous.Width);

                    Assert.True(gap >= 4,
                        $"Adjacent About tab headers are only {gap}px apart; expected at least 4px.");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void About_AllTabHeadersShareOneRowWithoutClipping()
    {
        // Issue #1204: five headers (with long lt labels) must fit on one row of the fixed window.
        var window = Show(new AboutWindow { DataContext = new AboutDialogViewModel() });
        try
        {
            var tabs = Assert.IsType<TabControl>(window.FindControl<Control>("MainTabs"));
            var strip = (Panel)tabs.ItemsPanelRoot!;

            var headers = strip.Children.OfType<TabItem>().ToArray();
            Assert.Equal(5, headers.Length);

            var firstY = headers[0].Bounds.Y;
            foreach (var header in headers)
            {
                Assert.True(Math.Abs(header.Bounds.Y - firstY) < 0.5,
                    $"Tab header at Y={header.Bounds.Y} is not on the same row as Y={firstY}; the strip wrapped.");
                Assert.True(header.Bounds.Width > 0,
                    "Tab header has no width; it is clipped.");
                Assert.True(header.Bounds.X + header.Bounds.Width <= strip.Bounds.Width + 1,
                    "Tab header extends past the strip; it is clipped.");
            }
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
}
