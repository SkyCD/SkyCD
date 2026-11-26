using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Guards that the Plugins → Settings repository list follows the shared Options list pattern:
/// right-aligned list actions, a bordered list with a bold header row and divided rows.
/// </summary>
public sealed class OptionsPluginsSettingsLayoutTests
{
    [Fact]
    public void RepositorySection_PlacesListActionsRightOfTheSectionHint()
    {
        var section = GetSettingsSection();
        var hint = Assert.Single(section.Descendants(),
            element => ((string?)element.Attribute("Text"))?.Contains("UiKeys.PluginRepositoryHint") == true);

        var actions = Assert.Single(section.Descendants().OfType<XElement>(),
            element => element.Name.LocalName == "StackPanel" &&
                       (string?)element.Attribute("Grid.Column") == "1" &&
                       element.Descendants().Any(child =>
                           (string?)child.Attribute("Command") == "{Binding AddPluginRepositoryCommand}"));
        Assert.Equal("Right", (string?)actions.Attribute("HorizontalAlignment"));
        Assert.Equal(hint.Parent, actions.Parent);

        foreach (var (command, tooltip, content) in new[]
                 {
                     ("AddPluginRepositoryCommand", "AddPluginRepositoryTooltip", "+"),
                     ("RemovePluginRepositoryCommand", "RemovePluginRepositoryTooltip", "-")
                 })
        {
            var button = Assert.Single(actions.Elements(),
                element => (string?)element.Attribute("Command") == $"{{Binding {command}}}");
            Assert.Equal(content, (string?)button.Attribute("Content"));
            Assert.Equal("{DynamicResource SkyFontSubtitle}", (string?)button.Attribute("FontSize"));
            Assert.Contains($"UiKeys.{tooltip}", (string?)button.Attribute("ToolTip.Tip"));
        }
    }

    [Fact]
    public void RepositorySection_RendersABorderedListWithHeaderRowAndDividedRows()
    {
        var section = GetSettingsSection();
        var list = Assert.Single(section.Descendants().OfType<XElement>(),
            element => element.Name.LocalName == "ListBox" &&
                       (string?)element.Attribute("ItemsSource") == "{Binding PluginRepositories}");
        Assert.Equal("Auto", (string?)list.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)list.Attribute("ScrollViewer.HorizontalScrollBarVisibility"));

        var container = list.Parent?.Parent;
        Assert.Equal("Border", container?.Name.LocalName);
        Assert.Equal("{DynamicResource ThemeBorderLowBrush}", (string?)container?.Attribute("BorderBrush"));
        Assert.Equal("1", (string?)container?.Attribute("BorderThickness"));

        var header = Assert.Single(container!.Descendants().OfType<XElement>(),
            element => element.Name.LocalName == "Border" &&
                       (string?)element.Attribute("BorderThickness") == "0,0,0,1" &&
                       element.Descendants().Any(child =>
                           ((string?)child.Attribute("Text"))?.Contains("UiKeys.PluginRepositoryColumn") == true));
        Assert.Equal("{DynamicResource SkyRowPadding}", (string?)header.Attribute("Padding"));
        Assert.Contains(header.Descendants(),
            element => ((string?)element.Attribute("Text"))?.Contains("UiKeys.Status") == true &&
                       (string?)element.Attribute("FontWeight") == "SemiBold");

        var row = Assert.Single(list.Descendants().OfType<XElement>(),
            element => element.Name.LocalName == "Border" &&
                       (string?)element.Attribute("BorderThickness") == "0,0,0,1" &&
                       element.Descendants().Any(child =>
                           (string?)child.Attribute("Text") == "{Binding Url, UpdateSourceTrigger=PropertyChanged}"));
        Assert.Equal("{DynamicResource SkyRowPadding}", (string?)row.Attribute("Padding"));

        var status = Assert.Single(row.Descendants().OfType<XElement>(),
            element => element.Name.LocalName == "StackPanel" &&
                       (string?)element.Attribute("Grid.Column") == "1" &&
                       element.Descendants().Any(child =>
                           (string?)child.Attribute("Text") == "{Binding StateLabel}"));
        var badge = Assert.Single(status.Descendants().OfType<XElement>(),
            element => element.Name.LocalName == "Border" &&
                       (string?)element.Attribute("IsVisible") == "{Binding IsBuiltIn}");
        Assert.Contains("UiKeys.PluginRepositoryBuiltIn", (string?)badge.Descendants()
            .First(child => child.Name.LocalName == "TextBlock").Attribute("Text"));

        var error = Assert.Single(row.Descendants(),
            element => (string?)element.Attribute("Text") == "{Binding Error}");
        Assert.Equal("{Binding HasError}", (string?)error.Attribute("IsVisible"));
        Assert.Equal("Wrap", (string?)error.Attribute("TextWrapping"));
        Assert.Equal("2", (string?)error.Attribute("Grid.ColumnSpan"));
    }

    [Fact]
    public void RepositorySection_KeepsLiteralTypographyAndSpacingOutOfTheMarkup()
    {
        var markup = GetSettingsSection().ToString();

        Assert.DoesNotMatch(new Regex("(FontSize|Spacing|RowSpacing|ColumnSpacing)=\"[0-9.]+\""), markup);
        Assert.DoesNotMatch(new Regex("(Margin|Padding)=\"[0-9.]+\""), markup);
    }

    private static XElement GetSettingsSection()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");
        var document = XDocument.Parse(xaml);
        return Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding ShowPluginsSettingsSection}");
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