using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #996: the Additional Info tab keeps one scrollable surface, a link block with the ↗ affordance,
/// and aligned caption/value rows whose labels are not selectable while values remain copyable.
/// </summary>
public sealed class OptionsPluginAdditionalInfoLayoutTests
{
    private const string AdditionalInfoHeader = "UiKeys.PluginTabAdditionalInfo";

    [Fact]
    public void BothDetailPanes_HaveAScrollableAdditionalInfoTab()
    {
        var tabs = GetAdditionalInfoTabs();

        Assert.Equal(2, tabs.Count);
        Assert.All(tabs, tab =>
        {
            var scroll = Assert.Single(tab.Elements(), element => element.Name.LocalName == "ScrollViewer");
            Assert.Equal("Disabled", (string?)scroll.Attribute("HorizontalScrollBarVisibility"));
            Assert.Contains(scroll.Elements(), element => element.Name.LocalName == "StackPanel");
        });
    }

    [Fact]
    public void InstalledTab_ShowsLinksAndTheMetadataRowsInOrder()
    {
        var tab = GetInstalledTab();

        Assert.Contains(tab.Descendants(), element =>
            element.Name.LocalName == "Button" &&
            (string?)element.Attribute("Classes") == "pluginLink" &&
            (string?)element.Attribute("Command") == "{Binding OpenSelectedPluginProjectUrlCommand}" &&
            (string?)element.Attribute("IsVisible") == "{Binding HasSelectedPluginDetailsProjectUrl}");
        Assert.Contains(tab.Descendants(), element =>
            (string?)element.Attribute("Command") == "{Binding OpenSelectedPluginAuthorUrlCommand}" &&
            (string?)element.Attribute("IsVisible") == "{Binding HasSelectedPluginDetailsAuthorUrl}");
        // The link affordance is the ↗ marker next to the translated label.
        Assert.Equal(2, tab.Descendants().Count(element => (string?)element.Attribute("Text") == "↗"));

        var grid = GetMetadataGrid(tab);
        Assert.Equal("Auto,*", (string?)grid.Attribute("ColumnDefinitions"));
        Assert.Equal("Auto,Auto,Auto,Auto,Auto,Auto,Auto", (string?)grid.Attribute("RowDefinitions"));
        Assert.Equal("{DynamicResource SkySpaceXs}", (string?)grid.Attribute("RowSpacing"));

        var labelKeys = new[]
        {
            "PluginVendorLabel", "PluginVersionLabel", "PluginSizeLabel", "PluginIdentityLabel",
            "Type", "PluginExtensionLabel", "PluginUpdatesFromLabel"
        };
        foreach (var key in labelKeys)
        {
            Assert.Contains(grid.Elements(), element =>
                element.Name.LocalName == "TextBlock" &&
                ((string?)element.Attribute("Text"))?.Contains($"UiKeys.{key}") == true);
        }

        var valueBindings = new[]
        {
            "SelectedPluginDetailsType", "SelectedPluginDetailsVersionNumber", "SelectedPluginDetailsSize",
            "SelectedPluginDetailsId", "SelectedPluginDetailsCapabilityType", "SelectedPluginDetailsExtension",
            "SelectedPluginDetailsUpdateSource"
        };
        foreach (var binding in valueBindings)
        {
            Assert.Contains(grid.Elements(), element =>
                element.Name.LocalName == "SelectableTextBlock" &&
                (string?)element.Attribute("Text") == $"{{Binding {binding}}}");
        }
    }

    [Fact]
    public void MetadataRows_HideTheirLabelAndValueWhenTheValueIsMissing()
    {
        var grid = GetMetadataGrid(GetInstalledTab());

        var optionalRows = new[]
        {
            "HasSelectedPluginDetailsType", "HasSelectedPluginDetailsVersionNumber", "HasSelectedPluginDetailsSize",
            "HasSelectedPluginDetailsCapabilityType", "HasSelectedPluginDetailsExtension",
            "HasSelectedPluginDetailsUpdateSource"
        };
        foreach (var row in optionalRows)
        {
            Assert.Equal(2, grid.Elements().Count(element =>
                (string?)element.Attribute("IsVisible") == $"{{Binding {row}}}"));
        }

        // The identifier is always present, so its row stays visible.
        Assert.DoesNotContain(grid.Elements(), element =>
            (string?)element.Attribute("Text") == "{Binding SelectedPluginDetailsId}" &&
            element.Attribute("IsVisible") is not null);
    }

    [Fact]
    public void Labels_AreCaptionsAndValuesStaySelectable()
    {
        var grid = GetMetadataGrid(GetInstalledTab());

        var captions = grid.Elements()
            .Where(element => element.Name.LocalName == "TextBlock" &&
                              ((string?)element.Attribute("Text"))?.StartsWith("{i18n:Translate", StringComparison.Ordinal) == true)
            .ToList();
        Assert.NotEmpty(captions);
        Assert.All(captions, caption => Assert.Equal("caption", (string?)caption.Attribute("Classes")));

        var values = grid.Elements().Where(element => element.Name.LocalName == "SelectableTextBlock").ToList();
        Assert.Equal(7, values.Count);
    }

    [Fact]
    public void MarketplaceTab_UsesTheSameShapeForTheFieldsItHas()
    {
        var tab = GetMarketplaceTab();

        Assert.Contains(tab.Descendants(), element =>
            (string?)element.Attribute("Command") == "{Binding OpenSelectedMarketplaceProjectUrlCommand}" &&
            (string?)element.Attribute("IsVisible") == "{Binding HasSelectedMarketplaceProjectUrl}");

        var grid = GetMetadataGrid(tab);
        Assert.Equal("Auto,Auto,Auto,Auto", (string?)grid.Attribute("RowDefinitions"));

        foreach (var key in new[]
                 {
                     "PluginPublisherLabel", "PluginSourceLabel", "PluginVersionLabel", "Status"
                 })
        {
            Assert.Contains(grid.Elements(), element =>
                ((string?)element.Attribute("Text"))?.Contains($"UiKeys.{key}") == true);
        }

        Assert.Contains(grid.Elements(), element =>
            (string?)element.Attribute("Text") == "{Binding SelectedMarketplacePlugin.Publisher}");
        Assert.Contains(grid.Elements(), element =>
            (string?)element.Attribute("Text") == "{Binding SelectedMarketplacePlugin.VersionSummary}");
        Assert.Contains(grid.Elements(), element =>
            (string?)element.Attribute("Text") == "{Binding SelectedMarketplacePlugin.StatusLabel}");
    }

    [Fact]
    public void AdditionalInfoTabs_KeepLiteralSpacingAndTypographyOut()
    {
        var markup = string.Concat(GetAdditionalInfoTabs().Select(tab => tab.ToString()));

        Assert.DoesNotMatch(new Regex("(FontSize|Spacing|RowSpacing|ColumnSpacing)=\"[0-9.]+\""), markup);
        Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{6,8}"), markup);
    }

    private static XElement GetMetadataGrid(XElement tab) =>
        Assert.Single(tab.Descendants(), element =>
            element.Name.LocalName == "Grid" &&
            (string?)element.Attribute("ColumnDefinitions") == "Auto,*");

    private static XElement GetInstalledTab() =>
        GetAdditionalInfoTabs().Single(tab =>
            tab.Descendants().Any(element =>
                (string?)element.Attribute("Text") == "{Binding SelectedPluginDetailsId}"));

    private static XElement GetMarketplaceTab() =>
        GetAdditionalInfoTabs().Single(tab =>
            tab.Descendants().Any(element =>
                (string?)element.Attribute("Text") == "{Binding SelectedMarketplacePlugin.Publisher}"));

    private static System.Collections.Generic.List<XElement> GetAdditionalInfoTabs()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Views", "OptionsWindow.axaml"));
        return XDocument.Parse(xaml).Descendants()
            .Where(element => element.Name.LocalName == "TabItem" &&
                              ((string?)element.Attribute("Header"))?.Contains(AdditionalInfoHeader) == true)
            .ToList();
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
