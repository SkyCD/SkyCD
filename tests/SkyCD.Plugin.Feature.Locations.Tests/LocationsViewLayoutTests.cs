using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.Plugin.Feature.Locations.Tests;

/// <summary>
/// Guards that the plugin's contributed Locations page follows the shared Options list pattern:
/// a right-aligned glyph action row, a bordered table with a bold header row and divided rows.
/// </summary>
public sealed class LocationsViewLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void LocationsView_ActionRow_UsesGlyphAddAndDeleteWithTooltips()
    {
        var document = XDocument.Parse(ReadView());

        var actions = Assert.Single(document.Descendants(Avalonia + "StackPanel"),
            panel => (string?)panel.Attribute("HorizontalAlignment") == "Right" &&
                     panel.Elements(Avalonia + "Button").Any(button =>
                         (string?)button.Attribute("Command") == "{Binding AddLocationCommand}"));
        Assert.Equal("Right", (string?)actions.Attribute("HorizontalAlignment"));

        foreach (var (command, tooltip, content) in new[]
                 {
                     ("AddLocationCommand", "AddLocationTooltip", "+"),
                     ("DeleteLocationCommand", "DeleteLocationTooltip", "-")
                 })
        {
            var button = Assert.Single(actions.Elements(Avalonia + "Button"),
                element => (string?)element.Attribute("Command") == $"{{Binding {command}}}");
            Assert.Equal(content, (string?)button.Attribute("Content"));
            Assert.Equal("34", (string?)button.Attribute("Width"));
            Assert.Equal("28", (string?)button.Attribute("Height"));
            Assert.Equal("{DynamicResource SkyFontSubtitle}", (string?)button.Attribute("FontSize"));
            Assert.Contains($"LocationsKeys.{tooltip}", (string?)button.Attribute("ToolTip.Tip"), StringComparison.Ordinal);
        }

        Assert.Contains(actions.Elements(Avalonia + "Button"),
            button => (string?)button.Attribute("Command") == "{Binding MergeLocationsCommand}");
    }

    [Fact]
    public void LocationsView_Table_HasBoldHeaderRowAndDividedRows()
    {
        var document = XDocument.Parse(ReadView());

        var table = Assert.Single(document.Descendants(Avalonia + "Border"),
            border => (string?)border.Attribute("BorderThickness") == "1" &&
                      (string?)border.Attribute("BorderBrush") == "{DynamicResource ThemeBorderLowBrush}" &&
                      border.Descendants(Avalonia + "ListBox").Any(list =>
                          (string?)list.Attribute("ItemsSource") == "{Binding Locations}"));

        var header = Assert.Single(table.Descendants(Avalonia + "Border"),
            border => (string?)border.Attribute("BorderThickness") == "0,0,0,1" &&
                      (string?)border.Attribute("Padding") == "{DynamicResource SkyRowPadding}" &&
                      border.Descendants(Avalonia + "TextBlock").Any(textBlock =>
                          ((string?)textBlock.Attribute("Text"))?.Contains("LocationsKeys.LocationName", StringComparison.Ordinal) == true));

        Assert.Contains(header.Descendants(Avalonia + "TextBlock"),
            textBlock => ((string?)textBlock.Attribute("Text"))?.Contains("LocationsKeys.LocationName", StringComparison.Ordinal) == true &&
                         (string?)textBlock.Attribute("FontWeight") == "SemiBold");
        Assert.Contains(header.Descendants(Avalonia + "TextBlock"),
            textBlock => ((string?)textBlock.Attribute("Text"))?.Contains("LocationsKeys.UsageColumn", StringComparison.Ordinal) == true &&
                         (string?)textBlock.Attribute("FontWeight") == "SemiBold");

        var row = Assert.Single(table.Descendants(Avalonia + "DataTemplate").Descendants(Avalonia + "Border"));
        Assert.Equal("0,0,0,1", (string?)row.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource SkyRowPadding}", (string?)row.Attribute("Padding"));
        Assert.Contains(row.Descendants(Avalonia + "TextBlock"),
            textBlock => (string?)textBlock.Attribute("Text") == "{Binding Name}");
        Assert.Contains(row.Descendants(Avalonia + "TextBlock"),
            textBlock => (string?)textBlock.Attribute("Text") == "{Binding UsageText}" &&
                         (string?)textBlock.Attribute("Classes") == "caption");
    }

    [Fact]
    public void LocationsView_KeepsLiteralTypographyAndSpacingOutOfTheMarkup()
    {
        var markup = ReadView();

        Assert.DoesNotMatch(new Regex("(FontSize|Spacing|RowSpacing|ColumnSpacing)=\"[0-9.]+\""), markup);
        Assert.DoesNotMatch(new Regex("(Margin|Padding)=\"[0-9.]+\""), markup);
    }

    private static string ReadView() =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "Plugins", "SkyCD.Plugin.Feature.Locations", "Views", "LocationsView.axaml"));

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
