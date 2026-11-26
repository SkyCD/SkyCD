using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Guards the JetBrains-style Options → Plug-ins page: one search/filter toolbar, a single scrollable
/// installed + marketplace list, and the two contextual detail panes with their tabs.
/// </summary>
public sealed class OptionsPluginsPageLayoutTests
{
    [Fact]
    public void PluginsPage_HasSearchBarWithUpdateAllAndFilterFlyout()
    {
        var page = GetPluginsPage();
        var markup = page.ToString();

        Assert.Contains("Text=\"{Binding PluginSearchText", markup);
        Assert.Single(page.Descendants(), element => element.Name.LocalName == "SearchTextBox");
        Assert.Contains("Command=\"{Binding UpdateAllPluginsCommand}\"", markup);

        // The settings flyout is gone; the path and repositories live on the Plugins settings page.
        var filterFlyout = Assert.Single(page.Descendants().OfType<XElement>(),
            element => element.Name.LocalName == "Flyout" &&
                       element.Descendants().Any(child =>
                           (string?)child.Attribute("Command") == "{Binding ShowAllPluginSectionsCommand}"));
        foreach (var command in new[]
                 {
                     "ShowInstalledPluginsOnlyCommand", "ShowMarketplacePluginsOnlyCommand"
                 })
        {
            Assert.Contains(filterFlyout.Descendants(),
                element => (string?)element.Attribute("Command") == $"{{Binding {command}}}");
        }
    }

    [Fact]
    public void PluginsPage_UsesOuterScrollWithTwoSectionHeaders()
    {
        var page = GetPluginsPage();

        var outerScroll = Assert.Single(page.Descendants().OfType<XElement>(),
            element => element.Name.LocalName == "ScrollViewer" &&
                       (string?)element.Attribute("VerticalScrollBarVisibility") == "Auto");
        var lists = outerScroll.Descendants().OfType<XElement>()
            .Where(element => element.Name.LocalName == "ListBox")
            .ToList();
        Assert.Equal(2, lists.Count);
        Assert.All(lists, list =>
            Assert.Equal("Disabled", (string?)list.Attribute("ScrollViewer.VerticalScrollBarVisibility")));

        Assert.Contains(page.Descendants(),
            element => (string?)element.Attribute("Text") == "{Binding InstalledPluginsHeader}");
        Assert.Contains(page.Descendants(),
            element => (string?)element.Attribute("Text") == "{Binding MarketplacePluginsHeader}");
        Assert.Contains(page.Descendants(),
            element => (string?)element.Attribute("Content") == "{Binding MarketplaceSectionToggleText}");
        Assert.Contains(page.Descendants(),
            element => element.Name.LocalName == "ProgressBar" &&
                       (string?)element.Attribute("IsVisible") == "{Binding IsMarketplaceLoading}");
        Assert.Contains(page.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding ShowMarketplacePaneEmptyState}");
    }

    [Fact]
    public void PluginsPage_BothListsDisableHorizontalScrollingSoRowsFitThePane()
    {
        var page = GetPluginsPage();

        var lists = page.Descendants().OfType<XElement>()
            .Where(element => element.Name.LocalName == "ListBox")
            .ToList();
        Assert.Equal(2, lists.Count);
        Assert.All(lists, list =>
            Assert.Equal("Disabled", (string?)list.Attribute("ScrollViewer.HorizontalScrollBarVisibility")));

        var outerScroll = Assert.Single(page.Descendants().OfType<XElement>(),
            element => element.Name.LocalName == "ScrollViewer" &&
                       (string?)element.Attribute("VerticalScrollBarVisibility") == "Auto");
        Assert.Equal("Disabled", (string?)outerScroll.Attribute("HorizontalScrollBarVisibility"));
    }

    [Fact]
    public void PluginsPage_EmptyStatesAreNonSelectableAndReplaceEmptyLists()
    {
        var page = GetPluginsPage();

        var installedEmpty = Assert.Single(page.Descendants(), element =>
            (string?)element.Attribute("Text") == "{i18n:Translate {x:Static i18n:TextKeys.PluginFilterNoMatches}}");
        Assert.Equal("{Binding ShowInstalledPluginsEmptyState}", (string?)installedEmpty.Attribute("IsVisible"));
        Assert.Empty(installedEmpty.Elements());

        var marketplaceFilterEmpty = Assert.Single(page.Descendants(), element =>
            (string?)element.Attribute("Text") ==
            "{i18n:Translate {x:Static i18n:TextKeys.PluginMarketplaceFilterNoMatches}}");
        Assert.Equal("{Binding ShowMarketplacePaneFilterEmptyState}",
            (string?)marketplaceFilterEmpty.Attribute("IsVisible"));

        foreach (var emptyState in new[] { installedEmpty, marketplaceFilterEmpty })
        {
            Assert.Equal("caption", (string?)emptyState.Attribute("Classes"));
            Assert.Equal("False", (string?)emptyState.Attribute("IsHitTestVisible"));
            Assert.Equal("Wrap", (string?)emptyState.Attribute("TextWrapping"));
            Assert.Equal("{DynamicResource ThemeForegroundLowBrush}", (string?)emptyState.Attribute("Foreground"));
            Assert.Equal("{DynamicResource SkyInsetSm}", (string?)emptyState.Attribute("Padding"));
        }

        var installedList = Assert.Single(page.Descendants(), element =>
            element.Name.LocalName == "ListBox" &&
            (string?)element.Attribute("ItemsSource") == "{Binding FilteredPlugins}");
        Assert.Equal("{Binding !ShowInstalledPluginsEmptyState}", (string?)installedList.Attribute("IsVisible"));

        var marketplaceList = Assert.Single(page.Descendants(), element =>
            element.Name.LocalName == "ListBox" &&
            (string?)element.Attribute("ItemsSource") == "{Binding FilteredMarketplacePlugins}");
        Assert.Equal("{Binding ShowMarketplacePaneList}", (string?)marketplaceList.Attribute("IsVisible"));
    }

    [Fact]
    public void PluginsPage_InstalledRowsToggleAndMarketplaceRowsExposeInstallAction()
    {
        var page = GetPluginsPage();

        var toggle = Assert.Single(page.Descendants().OfType<XElement>(),
            element => element.Name.LocalName == "ToggleSwitch");
        Assert.Equal("{Binding IsEnabled, Mode=TwoWay}", (string?)toggle.Attribute("IsChecked"));
        Assert.Equal(string.Empty, (string?)toggle.Attribute("OnContent"));
        Assert.Equal(string.Empty, (string?)toggle.Attribute("OffContent"));

        Assert.Contains(page.Descendants(),
            element => element.Name.LocalName == "Button" &&
                       (string?)element.Attribute("IsVisible") == "{Binding ShowPrimaryAction}" &&
                       ((string?)element.Attribute("Command"))?.Contains("RequestMarketplaceInstallCommand") == true &&
                       (string?)element.Attribute("CommandParameter") == "{Binding}");
    }

    [Fact]
    public void PluginsPage_DetailPanesExposeOverviewConfigurationAndAdditionalInfoTabs()
    {
        var page = GetPluginsPage();

        var tabControls = page.Descendants().OfType<XElement>()
            .Where(element => element.Name.LocalName == "TabControl")
            .ToList();
        Assert.Equal(2, tabControls.Count);

        var installedTabs = tabControls.Single(tabs => tabs.Descendants()
            .Any(element => (string?)element.Attribute("IsSelected") == "{Binding IsPluginConfigurationTabSelected, Mode=TwoWay}"));
        foreach (var key in new[] { "PluginTabOverview", "PluginTabConfiguration", "PluginTabAdditionalInfo" })
        {
            Assert.Contains(installedTabs.Descendants(),
                element => ((string?)element.Attribute("Header"))?.Contains($"UiKeys.{key}") == true);
        }

        var marketTabs = tabControls.Except([installedTabs]).Single();
        foreach (var key in new[] { "PluginTabOverview", "PluginTabAdditionalInfo" })
        {
            Assert.Contains(marketTabs.Descendants(),
                element => ((string?)element.Attribute("Header"))?.Contains($"UiKeys.{key}") == true);
        }
    }

    [Fact]
    public void PluginsPage_ReservesTheSameIconSlotOnEveryRowAndDetailHeader()
    {
        var page = GetPluginsPage();

        var slots = page.Descendants().OfType<XElement>()
            .Where(element => element.Name.LocalName == "Panel" &&
                              (string?)element.Attribute("Width") == (string?)element.Attribute("Height") &&
                              (string?)element.Attribute("Width") is "24" or "32" or "48")
            .ToList();
        Assert.Equal(4, slots.Count);

        var bitmapSlots = slots.Where(ContainsPluginBitmap).ToList();
        Assert.Equal(2, bitmapSlots.Count);
        Assert.Contains(bitmapSlots, slot => (string?)slot.Attribute("Width") == "32");
        Assert.Contains(bitmapSlots, slot => (string?)slot.Attribute("Width") == "48");

        var glyphSlots = slots.Except(bitmapSlots).ToList();
        Assert.Equal(2, glyphSlots.Count);
        Assert.All(glyphSlots, slot => Assert.Equal("24", (string?)slot.Attribute("Width")));

        foreach (var slot in slots)
        {
            var glyph = Assert.Single(slot.Descendants().OfType<XElement>(),
                element => element.Name.LocalName == "PackIconControl");

            if (ContainsPluginBitmap(slot))
            {
                var bitmap = Assert.Single(slot.Descendants().OfType<XElement>(),
                    element => element.Name.LocalName == "Image");
                Assert.Contains("PluginBitmapConverter", (string?)bitmap.Attribute("Source"));
                Assert.Equal("HighQuality",
                    (string?)bitmap.Attribute("RenderOptions.BitmapInterpolationMode"));
                Assert.Contains("PackIconLucideKind.Puzzle", (string?)glyph.Attribute("Kind"));
                Assert.Contains("!", (string?)glyph.Attribute("IsVisible"));

                if ((string?)slot.Attribute("Width") == "32")
                {
                    Assert.Contains("ListIcon", (string?)bitmap.Attribute("Source"));
                    Assert.Contains("HasListBitmapIcon", (string?)bitmap.Attribute("IsVisible"));
                    Assert.Contains("!HasListBitmapIcon", (string?)glyph.Attribute("IsVisible"));
                }
                else
                {
                    Assert.Contains("SelectedPlugin.Icon", (string?)bitmap.Attribute("Source"));
                    Assert.Contains("HasBitmapIcon", (string?)bitmap.Attribute("IsVisible"));
                    Assert.Contains("!SelectedPlugin.HasBitmapIcon", (string?)glyph.Attribute("IsVisible"));
                }
            }
            else
            {
                var bitmap = Assert.Single(slot.Descendants().OfType<XElement>(),
                    element => element.Name.LocalName == "Image");
                Assert.Contains("IconKey", (string?)bitmap.Attribute("Source"));
                Assert.Contains("HasBitmapIcon", (string?)bitmap.Attribute("IsVisible"));
                Assert.Contains("PluginGlyphConverter", (string?)glyph.Attribute("Kind"));
                Assert.Contains("IconKey", (string?)glyph.Attribute("Kind"));
                Assert.Contains("HasGlyphIcon", (string?)glyph.Attribute("IsVisible"));
            }
        }
    }

    private static bool ContainsPluginBitmap(XElement slot) =>
        slot.Descendants().OfType<XElement>().Any(element =>
            ((string?)element.Attribute("Source"))?.Contains("PluginBitmapConverter") == true);

    [Fact]
    public void PluginsPage_KeepsLiteralTypographyAndSpacingOutOfTheSplice()
    {
        var markup = GetPluginsPage().ToString();

        Assert.DoesNotMatch(new Regex("(FontSize|Spacing|RowSpacing|ColumnSpacing)=\"[0-9.]+\""), markup);
        Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{6,8}"), markup);
    }

    /// <summary>
    /// The plug-ins page occupies the old installed/marketplace block between the plugin list switch and the
    /// plugin settings section; slicing by those markers keeps the assertions independent of the rest of the window.
    /// </summary>
    private static XElement GetPluginsPage()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");
        var document = XDocument.Parse(xaml);
        var page = Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding ShowPluginListSection}");
        return page;
    }

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
