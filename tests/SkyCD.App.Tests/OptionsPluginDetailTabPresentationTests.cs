using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #997: the plugin detail Overview tabs rely on the shared card TabControl inset from #1000 and
/// present a missing description as a secondary caption in both the installed and marketplace panes.
/// </summary>
public sealed class OptionsPluginDetailTabPresentationTests
{
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";
    [Fact]
    public void PluginDetailTabControls_RelyOnTheSharedInsetAndAreNotRepaddedByTheWindow()
    {
        var (installedTabs, marketTabs) = GetDetailTabControls();

        AssertSharedInsetTheme();

        foreach (var tabs in new[] { installedTabs, marketTabs })
        {
            Assert.Null(tabs.Attribute("Padding"));
            Assert.Null(tabs.Attribute("Margin"));
        }

        foreach (var overviewTab in new[] { OverviewTab(installedTabs), OverviewTab(marketTabs) })
        {
            Assert.DoesNotContain(overviewTab.DescendantsAndSelf(),
                element => element.Attribute("Padding") is not null || element.Attribute("Margin") is not null);
        }
    }

    [Fact]
    public void InstalledOverview_ShowsARealDescriptionAsBodyTextAndThePlaceholderAsACaption()
    {
        var (installedTabs, _) = GetDetailTabControls();
        var descriptions = OverviewTab(installedTabs).Descendants().OfType<XElement>()
            .Where(element => element.Name.LocalName == "TextBlock" &&
                              (string?)element.Attribute("Text") == "{Binding SelectedPluginDescription}")
            .ToList();

        Assert.Equal(2, descriptions.Count);

        var body = Assert.Single(descriptions,
            element => (string?)element.Attribute("IsVisible") == "{Binding HasPluginDescription}");
        Assert.Null(body.Attribute("Classes"));

        var placeholder = Assert.Single(descriptions,
            element => (string?)element.Attribute("IsVisible") == "{Binding !HasPluginDescription}");
        Assert.Equal("caption", (string?)placeholder.Attribute("Classes"));
        Assert.Equal("{DynamicResource ThemeForegroundLowBrush}", (string?)placeholder.Attribute("Foreground"));
        Assert.Equal("Wrap", (string?)placeholder.Attribute("TextWrapping"));
    }

    [Fact]
    public void MarketplaceOverview_FallsBackToTheSameSecondaryCaptionInsteadOfAnEmptyArea()
    {
        var (_, marketTabs) = GetDetailTabControls();
        var textBlocks = OverviewTab(marketTabs).Descendants().OfType<XElement>()
            .Where(element => element.Name.LocalName == "TextBlock")
            .ToList();

        Assert.Contains(textBlocks,
            element => (string?)element.Attribute("Text") == "{Binding SelectedMarketplacePlugin.Summary}" &&
                       (string?)element.Attribute("IsVisible") == "{Binding SelectedMarketplacePlugin.HasSummary}");

        var placeholder = Assert.Single(textBlocks,
            element => ((string?)element.Attribute("IsVisible")) == "{Binding ShowMarketplacePluginSummaryEmptyState}");
        Assert.Contains("UiKeys.NoPluginDescription", (string?)placeholder.Attribute("Text"));
        Assert.Equal("caption", (string?)placeholder.Attribute("Classes"));
        Assert.Equal("{DynamicResource ThemeForegroundLowBrush}", (string?)placeholder.Attribute("Foreground"));
        Assert.Equal("Wrap", (string?)placeholder.Attribute("TextWrapping"));
    }

    private static void AssertSharedInsetTheme()
    {
        var styles = ReadRepoFile("src", "SkyCD.App", "Styles", "TabControlStyles.axaml");
        var document = XDocument.Parse(styles);

        var cardTheme = Assert.Single(document.Descendants(),
            element => element.Name.LocalName == "ControlTheme" &&
                       (string?)element.Attribute(Xaml2006 + "Key") == "CardTabControlTheme");
        Assert.Contains(cardTheme.Elements().OfType<XElement>(),
            setter => setter.Name.LocalName == "Setter" &&
                      (string?)setter.Attribute("Property") == "Padding" &&
                      (string?)setter.Attribute("Value") == "{DynamicResource SkyInsetMd}");

        Assert.Contains(document.Descendants(),
            element => element.Name.LocalName == "Style" &&
                       (string?)element.Attribute("Selector") == "TabControl[TabStripPlacement=Top]" &&
                       element.Descendants().Any(setter =>
                           (string?)setter.Attribute("Property") == "Theme" &&
                           (string?)setter.Attribute("Value") == "{StaticResource CardTabControlTheme}"));
    }

    private static (XElement Installed, XElement Market) GetDetailTabControls()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");
        var document = XDocument.Parse(xaml);
        var page = Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding ShowPluginListSection}");

        var tabControls = page.Descendants().OfType<XElement>()
            .Where(element => element.Name.LocalName == "TabControl")
            .ToList();
        Assert.Equal(2, tabControls.Count);

        var installed = Assert.Single(tabControls, tabs => TabItems(tabs).Count == 3);
        var market = Assert.Single(tabControls, tabs => TabItems(tabs).Count == 2);
        return (installed, market);
    }

    private static XElement OverviewTab(XElement tabs) => TabItems(tabs)[0];

    private static System.Collections.Generic.List<XElement> TabItems(XElement tabs) =>
        tabs.Elements().Where(element => element.Name.LocalName == "TabItem").ToList();

    private static string ReadRepoFile(params string[] parts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine([root, .. parts]));
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
