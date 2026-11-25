using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #937: the About window owns a single window inset and one spacing rhythm, so the
/// System info tab no longer shows a double inset, a wider gap above the cards, or misaligned values.
/// Issue #1147: the System info tab is a diagnostics dashboard with full-width bar rows and a
/// stretching card grid instead of fixed-width bars over empty space.
/// </summary>
public sealed class AboutWindowLayoutTests
{
    private const string WindowPaddingToken = "SkyWindowPadding";
    private const string FooterRowSpacing = "RowSpacing=\"{DynamicResource SkySpaceMd}\"";

    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Regex NonZeroMargin = new(
        "\\bMargin=\"[^\"]*[1-9]",
        RegexOptions.Compiled);

    [Fact]
    public void WindowPadding_IsAppliedExactlyOnce()
    {
        // A single inset owner: the DockPanel. Tab bodies must not repeat it.
        Assert.Equal(1, Occurrences(ReadAboutWindow(), WindowPaddingToken));
    }

    [Fact]
    public void Window_DoesNotDeclareLiteralMargins()
    {
        Assert.DoesNotMatch(NonZeroMargin, ReadAboutWindow());
    }

    [Fact]
    public void Footer_GapComesFromTheSpacingTokenInsteadOfAdHocMargins()
    {
        var about = ReadAboutWindow();

        Assert.Contains(FooterRowSpacing, about, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"0,12,0,0\"", about, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"0,4,0,0\"", about, StringComparison.Ordinal);
    }

    [Fact]
    public void SystemInfo_BarsStretchAcrossTheRowInsteadOfFixedWidths()
    {
        var systemInfo = SystemInfoTab();
        var bars = systemInfo.Descendants(Avalonia + "ProgressBar").ToArray();

        Assert.Equal(2, bars.Length);
        Assert.All(bars, bar =>
        {
            Assert.Null(bar.Attribute("Width"));
            Assert.Equal("Auto,*,Auto", (string?)bar.Parent!.Attribute("ColumnDefinitions"));
        });
        Assert.DoesNotContain("220", systemInfo.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SystemInfo_UsesAStretchingGridWithTheSectionGapToken()
    {
        var root = Assert.Single(SystemInfoTab().Elements());

        Assert.Equal("Grid", root.Name.LocalName);
        Assert.Equal("Auto,*", (string?)root.Attribute("RowDefinitions"));
        Assert.Equal("{DynamicResource SkySpaceSm}", (string?)root.Attribute("RowSpacing"));
    }

    [Fact]
    public void SystemInfo_CardGridHasFourStretchingDiagnosticCards()
    {
        var cardGrid = SystemInfoTab().Descendants(Avalonia + "Grid")
            .Single(grid => (string?)grid.Attribute("RowDefinitions") == "*,*");
        var cards = cardGrid.Elements(Avalonia + "ContentControl").ToArray();

        Assert.Equal("*,*", (string?)cardGrid.Attribute("ColumnDefinitions"));
        Assert.Equal(4, cards.Length);
        Assert.All(cards, card =>
        {
            Assert.Equal("{StaticResource DiagnosticCardTemplate}", (string?)card.Attribute("ContentTemplate"));
            Assert.Equal("Stretch", (string?)card.Attribute("HorizontalContentAlignment"));
            Assert.Equal("Stretch", (string?)card.Attribute("VerticalContentAlignment"));
        });
        Assert.Equal(4, cards.Select(card => (string?)card.Attribute("Content")).Distinct().Count());
    }

    [Fact]
    public void DiagnosticCards_UseTheSameInsetAndSpacingTokens()
    {
        var document = XDocument.Parse(ReadAboutWindow());
        var template = document.Descendants(Avalonia + "DataTemplate")
            .Single(element => (string?)element.Attribute(Xaml + "Key") == "DiagnosticCardTemplate");
        var border = Assert.Single(template.Elements(Avalonia + "Border"));
        var stack = Assert.Single(border.Elements(Avalonia + "StackPanel"));

        Assert.Equal("{DynamicResource SkyInsetXs}", (string?)border.Attribute("Padding"));
        Assert.Equal("{DynamicResource SkySpaceXxs}", (string?)stack.Attribute("Spacing"));
    }

    private static XElement SystemInfoTab()
    {
        return XDocument.Parse(ReadAboutWindow()).Descendants(Avalonia + "TabItem")
            .Single(tab => ((string?)tab.Attribute("Header"))?.Contains("UiKeys.SystemInfo", StringComparison.Ordinal) == true);
    }

    private static int Occurrences(string text, string token) =>
        text.Split(token, StringSplitOptions.None).Length - 1;

    private static string ReadAboutWindow()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Views", "AboutWindow.axaml");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with SkyCD.slnx was not found.");
    }
}
