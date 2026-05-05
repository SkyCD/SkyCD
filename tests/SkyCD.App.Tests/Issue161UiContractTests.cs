using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SkyCD.App.Tests;

public class Issue161UiContractTests
{
    [Fact]
    public void MainWindow_UsesSharedClassicToolbar()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");

        Assert.Contains("<cc:ClassicToolbar", xaml, StringComparison.Ordinal);
        Assert.Contains("<cc:DetailsListView", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedClassicToolbar_UsesItemsCollection()
    {
        var controlXaml = ReadRepoFile("src", "SkyCD.UI", "Controls", "Toolbars", "ClassicToolbar.axaml");
        var controlCode = ReadRepoFile("src", "SkyCD.UI", "Controls", "Toolbars", "ClassicToolbar.axaml.cs");
        var itemInterface = ReadRepoFile("src", "SkyCD.UI", "Controls", "Toolbars", "IClassicToolbarItem.cs");
        var buttonType = ReadRepoFile("src", "SkyCD.UI", "Controls", "Toolbars", "ClassicToolbarButton.cs");
        var separatorType = ReadRepoFile("src", "SkyCD.UI", "Controls", "Toolbars", "ClassicToolbarSeparator.cs");
        var appXaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");

        Assert.Contains("ItemsSource=\"{Binding Items, ElementName=Root}\"", controlXaml, StringComparison.Ordinal);
        Assert.Contains("AvaloniaList<IClassicToolbarItem>", controlCode, StringComparison.Ordinal);
        Assert.Contains("interface IClassicToolbarItem", itemInterface, StringComparison.Ordinal);
        Assert.Contains("IClassicToolbarItem", buttonType, StringComparison.Ordinal);
        Assert.Contains("IClassicToolbarItem", separatorType, StringComparison.Ordinal);
        Assert.Contains("<cc:ClassicToolbar.Items>", appXaml, StringComparison.Ordinal);
        Assert.Contains("<cc:ClassicToolbarButton", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PropertiesWindow_UsesSharedTabsAndPropertiesListControls()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");

        Assert.Contains("<TabControl", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"General\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Properties\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<cc:PropertiesList", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedPropertiesList_UsesDictionaryBasedPropertiesData()
    {
        var controlXaml = ReadRepoFile("src", "SkyCD.UI", "Controls", "Properties", "PropertiesList.axaml");
        var controlCode = ReadRepoFile("src", "SkyCD.UI", "Controls", "Properties", "PropertiesList.axaml.cs");
        var appXaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");

        Assert.Contains("ItemsSource=\"{Binding PropertiesRows, ElementName=Root}\"", controlXaml, StringComparison.Ordinal);
        Assert.Contains("PropertiesDataProperty", controlCode, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyDictionary<string, object?>", controlCode, StringComparison.Ordinal);
        Assert.DoesNotContain("<Button", controlXaml, StringComparison.Ordinal);
        Assert.Contains("PropertiesData=\"{Binding InfoProperties}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("PropertyHeader=\"Property\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ValueHeader=\"Value\"", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCustomControls_AvoidHardcodedColorLiterals()
    {
        var files = new[]
        {
            ReadRepoFile("src", "SkyCD.UI", "Controls", "Toolbars", "ClassicToolbar.axaml"),
            ReadRepoFile("src", "SkyCD.UI", "Controls", "Properties", "PropertiesList.axaml"),
            ReadRepoFile("src", "SkyCD.UI", "Controls", "Lists", "DetailsListView.axaml")
        };

        foreach (var xaml in files)
        {
            Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{3,8}"), xaml);
        }
    }

    [Fact]
    public void SharedDetailsListView_IsGenericAndTemplateDriven()
    {
        var controlXaml = ReadRepoFile("src", "SkyCD.UI", "Controls", "Lists", "DetailsListView.axaml");
        var controlCode = ReadRepoFile("src", "SkyCD.UI", "Controls", "Lists", "DetailsListView.axaml.cs");
        var appXaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");

        Assert.Contains("HeaderContent", controlXaml, StringComparison.Ordinal);
        Assert.Contains("RowTemplate", controlXaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSourceProperty", controlCode, StringComparison.Ordinal);
        Assert.Contains("SelectedItemProperty", controlCode, StringComparison.Ordinal);
        Assert.Contains("ListContextMenuProperty", controlCode, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Name\"", controlXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Type\"", controlXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"Size\"", controlXaml, StringComparison.Ordinal);
        Assert.Contains("<cc:DetailsListView.HeaderContent>", appXaml, StringComparison.Ordinal);
        Assert.Contains("<cc:DetailsListView.RowTemplate>", appXaml, StringComparison.Ordinal);
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
