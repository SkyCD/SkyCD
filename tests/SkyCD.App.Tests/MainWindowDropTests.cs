using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1034: the main window owns OS file drops. The handlers are declared in XAML on the window
/// itself so drops over the catalog tree or browser are not swallowed by the nested lists.
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
    public void DropOverlay_StartsHiddenAndDoesNotBlockHitTesting()
    {
        var overlay = GetDropOverlay();

        Assert.Equal("False", (string?)overlay.Attribute("IsVisible"));
        Assert.Equal("False", (string?)overlay.Attribute("IsHitTestVisible"));
        Assert.Equal("3", (string?)overlay.Attribute("Grid.ColumnSpan"));
        Assert.Contains("UiKeys.DropToAdd", overlay.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DropOverlay_LivesInTheMainLayoutGrid()
    {
        var window = GetWindow();
        var layout = Assert.Single(window.Descendants(Avalonia + "Grid"),
            grid => (string?)grid.Attribute(Xaml2006 + "Name") == "MainLayoutGrid");

        Assert.Contains(layout.Elements(Avalonia + "Border"),
            border => (string?)border.Attribute(Xaml2006 + "Name") == "DropOverlay");
    }

    private static XElement GetWindow() =>
        XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml")).Root!;

    private static XElement GetDropOverlay() =>
        Assert.Single(GetWindow().Descendants(Avalonia + "Border"),
            border => (string?)border.Attribute(Xaml2006 + "Name") == "DropOverlay");

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
