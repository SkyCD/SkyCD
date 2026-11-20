using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

public class OptionsWindowLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void CustomPropertiesSection_UsesFullHeightStatusesGridLayout()
    {
        var section = GetCustomPropertiesSection();

        Assert.Equal("Grid", section.Name.LocalName);
        Assert.Equal("Auto,Auto,*,Auto", (string?)section.Attribute("RowDefinitions"));
        Assert.Equal("8", (string?)section.Attribute("RowSpacing"));
    }

    [Fact]
    public void CustomPropertiesSection_HasHeaderWithDividerAndRightAlignedToolbar()
    {
        var section = GetCustomPropertiesSection();
        var toolbar = Assert.Single(section.Descendants(Avalonia + "StackPanel"),
            panel => (string?)panel.Attribute("HorizontalAlignment") == "Right");
        var buttons = toolbar.Elements(Avalonia + "Button").ToArray();

        Assert.Equal(2, buttons.Length);
        Assert.All(buttons, button =>
        {
            Assert.Equal("34", (string?)button.Attribute("Width"));
            Assert.Equal("28", (string?)button.Attribute("Height"));
            Assert.Equal("16", (string?)button.Attribute("FontSize"));
        });
        Assert.Contains("AddCustomPropertyCommand", buttons[0].ToString(), StringComparison.Ordinal);
        Assert.Contains("RemoveCustomPropertyCommand", buttons[1].ToString(), StringComparison.Ordinal);
        Assert.Contains(section.Descendants(Avalonia + "Border"),
            border => (string?)border.Attribute("Height") == "1" && (string?)border.Attribute("Background") == "#2E2E2E");
    }

    [Fact]
    public void CustomPropertiesSection_UsesBorderedTableWithSemiBoldColumnHeaders()
    {
        var section = GetCustomPropertiesSection();
        var table = Assert.Single(section.Descendants(Avalonia + "Border"),
            border => (string?)border.Attribute("BorderThickness") == "1" && (string?)border.Attribute("BorderBrush") == "#2E2E2E");
        var headers = table.Descendants(Avalonia + "TextBlock")
            .Where(textBlock => (string?)textBlock.Attribute("FontWeight") == "SemiBold")
            .ToArray();

        Assert.Equal(2, headers.Length);
        Assert.Contains(headers, header => header.ToString().Contains("UiKeys.PropertyName", StringComparison.Ordinal));
        Assert.Contains(headers, header => header.ToString().Contains("UiKeys.DefaultValue", StringComparison.Ordinal));
    }

    [Fact]
    public void CustomPropertiesSection_ListFillsAvailableSpaceWithoutFixedHeight()
    {
        var section = GetCustomPropertiesSection();
        var list = Assert.Single(section.Descendants(Avalonia + "ListBox"));

        Assert.Null(list.Attribute("MaxHeight"));
        Assert.Equal("Auto", (string?)list.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
    }

    [Fact]
    public void CustomPropertiesSection_ItemRowsKeepTwoWayEditingInBottomBorderedRows()
    {
        var section = GetCustomPropertiesSection();
        var template = Assert.Single(section.Descendants(Avalonia + "DataTemplate"));
        var row = Assert.Single(template.Elements(Avalonia + "Border"));
        var textBoxes = template.Descendants(Avalonia + "TextBox").ToArray();

        Assert.Equal("0,0,0,1", (string?)row.Attribute("BorderThickness"));
        Assert.Equal("8,6", (string?)row.Attribute("Padding"));
        Assert.Equal(2, textBoxes.Length);
        Assert.Contains("Binding Name", textBoxes[0].ToString(), StringComparison.Ordinal);
        Assert.Contains("Binding DefaultValue", textBoxes[1].ToString(), StringComparison.Ordinal);
        Assert.All(textBoxes, textBox => Assert.Contains("Mode=TwoWay", textBox.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void CustomPropertiesSection_AlertOnlyAppearsWhenValidationFails()
    {
        var section = GetCustomPropertiesSection();
        var alert = Assert.Single(section.Descendants(Avalonia + "Border"),
            border => (string?)border.Attribute("IsVisible") == "{Binding ShowCustomPropertyAlert}");

        Assert.Contains("CustomPropertyAlertMessage", alert.ToString(), StringComparison.Ordinal);
    }

    private static XElement GetCustomPropertiesSection()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");
        var document = XDocument.Parse(xaml);

        return Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding ShowCustomPropertiesSection}");
    }

    [Fact]
    public void ToolbarSection_HostsTheToolbarCustomizationControls()
    {
        var section = GetToolbarSection();
        Assert.Equal("Auto,Auto,*", (string?)section.Attribute("RowDefinitions"));
        Assert.Contains("UiKeys.CustomizeToolbarHint", section.ToString(), StringComparison.Ordinal);

        var commands = section.Descendants(Avalonia + "Button")
            .Select(button => button.Attribute("Command")?.Value ?? string.Empty)
            .ToArray();
        Assert.Equal(
        [
            "{Binding Toolbar.AddCommand}",
            "{Binding Toolbar.AddSeparatorCommand}",
            "{Binding Toolbar.RemoveCommand}",
            "{Binding Toolbar.MoveUpCommand}",
            "{Binding Toolbar.MoveDownCommand}",
            "{Binding Toolbar.ResetCommand}"
        ], commands);
        Assert.Contains("Toolbar.AvailableCommands", section.ToString(), StringComparison.Ordinal);
        Assert.Contains("Toolbar.SelectedItems", section.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ToolbarCategory_ShipsForEverySupportedCulture()
    {
        foreach (var culture in new[] { "en", "lt" })
        {
            var yaml = ReadRepoFile("src", "SkyCD.UI", "Translations", $"{culture}-UI.yaml");

            Assert.Contains("ui.Toolbar:", yaml, StringComparison.Ordinal);
        }
    }

    private static XElement GetToolbarSection()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");
        var document = XDocument.Parse(xaml);

        return Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding ShowToolbarSection}");
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
