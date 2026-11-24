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
/// </summary>
public sealed class AboutWindowLayoutTests
{
    private const string WindowPaddingToken = "SkyWindowPadding";
    private const string SharedValueColumn = "ColumnDefinitions=\"220,*\"";
    private const string SmallStackSpacing = "Spacing=\"{DynamicResource SkySpaceXs}\"";
    private const string FooterRowSpacing = "RowSpacing=\"{DynamicResource SkySpaceMd}\"";

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
    public void SystemInfo_RowsShareOneFixedValueColumnWithoutFixedBarWidths()
    {
        var about = ReadAboutWindow();

        Assert.Equal(2, Occurrences(about, SharedValueColumn));
        Assert.Equal(0, Occurrences(about, "Width=\"220\""));
    }

    [Fact]
    public void Cards_UseTheSameInnerSpacingRhythm()
    {
        Assert.True(Occurrences(ReadAboutWindow(), SmallStackSpacing) >= 2);
    }

    [Fact]
    public void SystemInfo_SectionsUseTheSectionGapToken()
    {
        var document = XDocument.Parse(ReadAboutWindow());
        XNamespace avalonia = "https://github.com/avaloniaui";
        var systemInfo = document.Descendants(avalonia + "TabItem")
            .Single(tab => ((string?)tab.Attribute("Header"))?.Contains("UiKeys.SystemInfo", StringComparison.Ordinal) == true)
            .Elements().First();

        Assert.Equal("StackPanel", systemInfo.Name.LocalName);
        Assert.Equal("{DynamicResource SkySpaceMd}", (string?)systemInfo.Attribute("Spacing"));
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
