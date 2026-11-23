using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1000: every card-style tab surface gets one content inset from the shared TabControl
/// theme, so windows no longer pad tab bodies themselves.
/// </summary>
public sealed class TabControlContentInsetTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void CardTabTheme_DeclaresTheSharedContentInset()
    {
        var styles = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Styles", "TabControlStyles.axaml"));
        var theme = Assert.Single(styles.Descendants(Avalonia + "ControlTheme"),
            element => (string?)element.Attribute(Xaml2006 + "Key") == "CardTabControlTheme");

        var padding = Assert.Single(theme.Elements(Avalonia + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Padding");

        Assert.Equal("{DynamicResource SkyInsetMd}", (string?)padding.Attribute("Value"));
    }

    [Fact]
    public void App_YieldsTabContentToTheSharedInset()
    {
        var app = ReadRepoFile("src", "SkyCD.App", "App.axaml");

        Assert.Contains("Styles/TabControlStyles.axaml", app, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AboutWindow.axaml")]
    [InlineData("OptionsWindow.axaml")]
    [InlineData("PropertiesWindow.axaml")]
    public void TabBodies_DoNotRepeatTheInsetPerWindow(string window)
    {
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", window));

        foreach (var tab in document.Descendants(Avalonia + "TabItem"))
        {
            foreach (var body in TabBodies(tab))
            {
                Assert.Null(body.Attribute("Margin"));
                Assert.Null(body.Attribute("Padding"));
            }
        }
    }

    private static IEnumerable<XElement> TabBodies(XElement tab) =>
        tab.Elements().Where(element => !element.Name.LocalName.Contains('.'));

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
