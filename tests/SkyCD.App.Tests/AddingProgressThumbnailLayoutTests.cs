using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #944: the adding progress window shows the newest thumbnail above the error log, hidden until
/// one has been produced, and keeps the localized caption and design tokens.
/// </summary>
public sealed class AddingProgressThumbnailLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ThumbnailSection_SitsAboveTheErrorLogAndCollapsesUntilAThumbnailArrives()
    {
        var grid = RootGrid();

        var section = Assert.Single(grid.Elements(Avalonia + "StackPanel"),
            element => ContainsText(element, "UiKeys.LatestThumbnail}"));
        var errorLog = Assert.Single(grid.Elements(Avalonia + "Grid"),
            element => ContainsText(element, "UiKeys.ErrorLog}"));

        Assert.Equal("IsVisible=\"{Binding HasLatestThumbnail}\"", section.Attribute("IsVisible")?.ToString());
        Assert.Equal("3", (string?)section.Attribute("Grid.Row"));
        Assert.Equal("4", (string?)errorLog.Attribute("Grid.Row"));

        var image = Assert.Single(section.Descendants(Avalonia + "Image"));
        Assert.Equal("Source=\"{Binding LatestThumbnail}\"", image.Attribute("Source")?.ToString());
        Assert.Equal("Uniform", (string?)image.Attribute("Stretch"));

        var border = Assert.Single(section.Elements(Avalonia + "Border"));
        Assert.Equal("96", (string?)border.Attribute("Height"));
        Assert.NotNull(border.Attribute("BorderBrush"));
    }

    [Fact]
    public void ThumbnailSection_UsesLocalizedCaptionAndDesignTokens()
    {
        var section = Assert.Single(RootGrid().Elements(Avalonia + "StackPanel"),
            element => ContainsText(element, "UiKeys.LatestThumbnail}"));

        var caption = Assert.Single(section.Elements(Avalonia + "TextBlock"));
        Assert.Equal("Text=\"{i18n:Translate {x:Static i18n:UiKeys.LatestThumbnail}}\"",
            caption.Attribute("Text")?.ToString());
        Assert.Equal("SemiBold", (string?)caption.Attribute("FontWeight"));

        var xaml = section.ToString();
        Assert.DoesNotContain("FontSize=", xaml, StringComparison.Ordinal);
        Assert.DoesNotMatch("(Spacing|RowSpacing|ColumnSpacing)=\"[-0-9.]", xaml);
        Assert.DoesNotContain("#", xaml, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SkySpaceXs}", xaml, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource SkyInsetXs}", xaml, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource ThemeBorderLowBrush}", xaml, StringComparison.Ordinal);
        Assert.Contains("{DynamicResource ThemeBackgroundBrush}", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorLogKeepsItsRowAndTheFooterIsPinnedBelowTheFixedContent()
    {
        var grid = RootGrid();

        Assert.Equal("Auto,Auto,Auto,Auto,*", (string?)grid.Attribute("RowDefinitions"));

        var parent = grid.Parent!;
        Assert.Equal("Grid", parent.Name.LocalName);
        Assert.Equal("*,Auto", (string?)parent.Attribute("RowDefinitions"));

        var footer = Assert.Single(parent.Elements(Avalonia + "Grid"),
            element => ContainsText(element, "UiKeys.Cancel}"));
        Assert.Equal("1", (string?)footer.Attribute("Grid.Row"));
    }

    /// <summary>
    /// Issue #1153: fixed-size dialog — the whole content no longer sits in an outer scroller;
    /// only the error log scrolls, inside its own border.
    /// </summary>
    [Fact]
    public void FixedDialog_HasNoOuterScroller()
    {
        var viewerNames = WindowDocument().Descendants(Avalonia + "ScrollViewer")
            .Select(viewer => (string?)viewer.Attribute(Xaml + "Name"))
            .ToList();

        Assert.DoesNotContain("ProgressContentScroller", viewerNames);
        Assert.Contains("LogScrollViewer", viewerNames);
    }

    [Fact]
    public void FixedDialog_CannotResize()
    {
        var window = WindowDocument();

        Assert.Equal("False", (string?)window.Attribute("CanResize"));
        Assert.Null(window.Attribute("MinWidth"));
        Assert.Null(window.Attribute("MinHeight"));
    }

    private static XElement WindowDocument()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Views",
            "AddingProgressWindow.axaml"));
        return XDocument.Parse(xaml).Root!;
    }

    private static XElement RootGrid()
    {
        var window = WindowDocument();
        return window.Descendants(Avalonia + "Grid")
            .Single(grid => ((string?)grid.Attribute("RowDefinitions")) == "Auto,Auto,Auto,Auto,*");
    }

    private static bool ContainsText(XElement element, string text) =>
        element.DescendantsAndSelf()
            .SelectMany(descendant => descendant.Attributes())
            .Any(attribute => attribute.Value.Contains(text, StringComparison.Ordinal));

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
