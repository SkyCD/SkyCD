using System;
using System.IO;
using Xunit;

namespace SkyCD.App.Tests;

public class MainWindowLayoutParityTests
{
    [Fact]
    public void MainWindow_MatchesLegacyInitialProportionsAndSplitter()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");

        Assert.Contains("Width=\"503\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"273\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"320\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"200\"", xaml, StringComparison.Ordinal);

        Assert.Contains("<ColumnDefinition Width=\"121\" MinWidth=\"50\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"3\" />", xaml, StringComparison.Ordinal);

        Assert.Contains("<GridSplitter Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"3\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Background=\"{DynamicResource ThemeControlMidBrush}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#CFCFCF\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_TreeView_ProvidesScrollingAndTooltips()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");

        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip.Tip=\"{Binding Title}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"16\" Height=\"16\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void TreeViewStyles_ProvidesLegacyVisualTreatmentAndThemeIntegration()
    {
        var stylesXaml = ReadRepoFile("src", "SkyCD.App", "Styles", "TreeViewStyles.axaml");
        var appXaml = ReadRepoFile("src", "SkyCD.App", "App.axaml");

        Assert.Contains("<StyleInclude Source=\"avares://SkyCD.App/Styles/TreeViewStyles.axaml\" />", appXaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"TreeViewItem\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"MinHeight\" Value=\"24\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"TreeViewItem:selected /template/ Border#SelectionBorder\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"TreeViewItem:selected:focus", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{DynamicResource TreeViewActiveSelectionBackgroundBrush}\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{DynamicResource TreeViewActiveSelectionForegroundBrush}\"", stylesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Selector=\"TreeViewItem:pointerover /template/ Border#SelectionBorder\"", stylesXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_RestoresSavedTreePaneWidth_WhenValid()
    {
        var csCode = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml.cs");

        Assert.Contains("if (options.Window.TreePaneWidth is > 0)", csCode, StringComparison.Ordinal);
        Assert.DoesNotContain("if (options.Window.TreePaneWidth is >= 160)", csCode, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_Toolbar_MatchesLegacyCommandArtworkAndCompactStructure()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");

        Assert.Contains("ImageSrc=\"/Assets/new.png\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ImageSrc=\"/Assets/open.png\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ImageSrc=\"/Assets/save.png\"", xaml, StringComparison.Ordinal);

        Assert.DoesNotContain("ImageSrc=\"/Assets/add-from-media.png\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageSrc=\"/Assets/add-from-folder.png\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ImageSrc=\"/Assets/add-from-internet.png\"", xaml, StringComparison.Ordinal);

        // Ensure 68-DIP minwidth override and wide padding were removed from the view
        Assert.DoesNotContain("MinWidth=\"68\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ButtonStyles_ToolbarButton_ProvidesPaintedSurfaceAndAccessKeys()
    {
        var stylesXaml = ReadRepoFile("src", "SkyCD.App", "Styles", "ButtonStyles.axaml");
        var appXaml = ReadRepoFile("src", "SkyCD.App", "App.axaml");

        Assert.Contains("<StyleInclude Source=\"avares://SkyCD.App/Styles/ButtonStyles.axaml\" />", appXaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ct|ClassicToolbarButton\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("<AccessText", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("Border Name=\"PART_ButtonBorder\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ct|ClassicToolbarButton:pointerover\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ct|ClassicToolbarButton:pressed\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ct|ClassicToolbarButton:focus-visible\"", stylesXaml, StringComparison.Ordinal);
        Assert.Contains("Selector=\"ct|ClassicToolbarButton:disabled\"", stylesXaml, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine([root, .. parts]);
        return File.ReadAllText(fullPath);
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
