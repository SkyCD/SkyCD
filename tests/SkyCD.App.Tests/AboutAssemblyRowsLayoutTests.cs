using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #938: the Loaded assemblies row separator belongs to the list item container so it spans
/// the full row (and selection highlight) at the row bottom, with natural (not fixed) row heights.
/// </summary>
public sealed class AboutAssemblyRowsLayoutTests
{
    private static readonly Regex ListBoxItemStyle = new(
        "<Style\\s+Selector=\"ListBoxItem\">(?<body>.*?)</Style>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ListBoxItemSelector = new(
        "<Style\\s+Selector=\"ListBoxItem\">",
        RegexOptions.Compiled);

    private static readonly Regex BottomSeparator = new(
        "<Setter\\s+Property=\"BorderThickness\"\\s+Value=\"0,0,0,1\"\\s*/?>",
        RegexOptions.Compiled);

    private static readonly Regex StretchContent = new(
        "<Setter\\s+Property=\"HorizontalContentAlignment\"\\s+Value=\"Stretch\"\\s*/?>",
        RegexOptions.Compiled);

    private static readonly Regex ThemeBorderBrush = new(
        "<Setter\\s+Property=\"BorderBrush\"\\s+Value=\"{DynamicResource ThemeBorderLowBrush}\"\\s*/?>",
        RegexOptions.Compiled);

    private static readonly Regex PaddedContainer = new(
        "<Setter\\s+Property=\"Padding\"\\s+Value=\"{DynamicResource SkyInsetSm}\"\\s*/?>",
        RegexOptions.Compiled);

    [Fact]
    public void About_DoesNotImposeAssemblyRowHeight()
    {
        var about = ReadRepoFile("src", "SkyCD.App", "Views", "AboutWindow.axaml");

        Assert.DoesNotContain("MinHeight", about, StringComparison.Ordinal);
    }

    [Fact]
    public void About_DeclaresExactlyOneListBoxItemStyle()
    {
        var about = ReadRepoFile("src", "SkyCD.App", "Views", "AboutWindow.axaml");

        Assert.Single(ListBoxItemSelector.Matches(about));
    }

    [Fact]
    public void ListBoxItemStyle_OwnsTheBottomSeparator()
    {
        var style = SingleListBoxItemStyle();

        Assert.Matches(BottomSeparator, style);
        Assert.Matches(StretchContent, style);
        Assert.Matches(ThemeBorderBrush, style);
    }

    [Fact]
    public void ItemTemplate_NoLongerWrapsRowsInABorderedContainer()
    {
        var about = ReadRepoFile("src", "SkyCD.App", "Views", "AboutWindow.axaml");

        Assert.Single(BottomSeparator.Matches(about));
        Assert.Contains("SkySpaceXs", about, StringComparison.Ordinal);
    }

    [Fact]
    public void ListBoxItemStyle_UsesTheInsetPaddingToken()
    {
        var style = SingleListBoxItemStyle();

        Assert.Matches(PaddedContainer, style);
    }

    private static string SingleListBoxItemStyle()
    {
        var about = ReadRepoFile("src", "SkyCD.App", "Views", "AboutWindow.axaml");
        var matches = ListBoxItemStyle.Matches(about);

        Assert.Single(matches);

        return matches[0].Groups["body"].Value;
    }

    private static string ReadRepoFile(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepoRoot(), .. parts]));

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
