using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1034: the main window owns OS file drops. Issue #1060: the destination is the container
/// under the pointer (highlighted), not the tree selection, and the window-wide overlay must not
/// hide the tree/list or the OS drag image.
/// </summary>
public class MainWindowDropTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Window_DeclaresTheOsDropHandlers()
    {
        var window = GetWindow();

        Assert.Equal("True", (string?)window.Attribute("DragDrop.AllowDrop"));
        Assert.Equal("OnWindowDragOver", (string?)window.Attribute("DragDrop.DragEnter"));
        Assert.Equal("OnWindowDragOver", (string?)window.Attribute("DragDrop.DragOver"));
        Assert.Equal("OnWindowDragLeave", (string?)window.Attribute("DragDrop.DragLeave"));
        Assert.Equal("OnWindowDrop", (string?)window.Attribute("DragDrop.Drop"));
    }

    [Fact]
    public void DropHint_IsATopChipThatDoesNotBlockHitTesting()
    {
        var hint = GetDropHint();

        Assert.Equal("False", (string?)hint.Attribute("IsVisible"));
        Assert.Equal("False", (string?)hint.Attribute("IsHitTestVisible"));
        Assert.Equal("Center", (string?)hint.Attribute("HorizontalAlignment"));
        Assert.Equal("Top", (string?)hint.Attribute("VerticalAlignment"));
        // Root is the only implicit parent, so the chip says where an empty-space drop lands.
        Assert.Contains("UiKeys.DropToAddToLibrary", hint.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DropHint_LivesInTheMainLayoutGrid()
    {
        var window = GetWindow();
        var layout = Assert.Single(window.Descendants(Avalonia + "Grid"),
            grid => (string?)grid.Attribute(Xaml2006 + "Name") == "MainLayoutGrid");

        Assert.Contains(layout.Elements(Avalonia + "Border"),
            border => (string?)border.Attribute(Xaml2006 + "Name") == "DropHint");
    }

    [Fact]
    public void DropTargetHighlight_ReusesTheSelectionTreatment()
    {
        var treeStyles = ReadRepoFile("src", "SkyCD.App", "Styles", "TreeViewStyles.axaml");
        var details = ReadRepoFile("src", "SkyCD.UI", "Controls", "Lists", "DetailsListView.axaml");
        var browser = ReadRepoFile("src", "SkyCD.UI", "Controls", "Lists", "BrowserItemsView.axaml");

        Assert.Contains("TreeViewItem.drop-target", treeStyles, StringComparison.Ordinal);
        Assert.Contains("Border#SelectionBorder", treeStyles, StringComparison.Ordinal);
        Assert.Contains("ListBoxItem.drop-target", details, StringComparison.Ordinal);
        Assert.Contains("ListBoxItem.drop-target", browser, StringComparison.Ordinal);
    }

    private static XElement GetWindow() =>
        XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml")).Root!;

    private static XElement GetDropHint() =>
        Assert.Single(GetWindow().Descendants(Avalonia + "Border"),
            border => (string?)border.Attribute(Xaml2006 + "Name") == "DropHint");

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
