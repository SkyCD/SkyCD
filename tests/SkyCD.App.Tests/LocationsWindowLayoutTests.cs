using System;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

public class LocationsWindowLayoutTests
{
    [Fact]
    public void Dialog_IsFixedSizeAndCenteredOnOwner()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "LocationsWindow.axaml");
        var window = XDocument.Parse(xaml).Root!;

        Assert.Equal("560", (string?)window.Attribute("Width"));
        Assert.Equal("480", (string?)window.Attribute("Height"));
        Assert.Equal("False", (string?)window.Attribute("CanResize"));
        Assert.Equal("False", (string?)window.Attribute("CanMinimize"));
        Assert.Equal("False", (string?)window.Attribute("ShowInTaskbar"));
        Assert.Equal("CenterOwner", (string?)window.Attribute("WindowStartupLocation"));
        Assert.Equal("/Assets/skycd.ico", (string?)window.Attribute("Icon"));
    }

    [Fact]
    public void Dialog_PreservesManagerCommandsAndResults()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "LocationsWindow.axaml");

        Assert.Contains("x:DataType=\"vm:LocationsDialogViewModel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding CreateLocationCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RenameLocationCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding MergeLocationsCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DeleteLocationCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ShowItemsCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding NavigateToSelectedCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ConfirmCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Locations}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StringFormat='({0})'", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding MergeTarget, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
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

        throw new DirectoryNotFoundException("Repository root with SkyCD.slnx was not found.");
    }
}
