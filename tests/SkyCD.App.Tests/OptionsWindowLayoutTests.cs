using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

public class OptionsWindowLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void CustomPropertiesSection_UsesFullHeightStatusesGridLayout()
    {
        var section = GetCustomPropertiesSection();

        Assert.Equal("Grid", section.Name.LocalName);
        Assert.Equal("Auto,Auto,*,Auto", (string?)section.Attribute("RowDefinitions"));
        Assert.Equal("{DynamicResource SkySpaceSm}", (string?)section.Attribute("RowSpacing"));
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
            Assert.Equal("{DynamicResource SkyFontSubtitle}", (string?)button.Attribute("FontSize"));
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

    [Fact]
    public void AiDescriptionSection_UsesFullHeightGridWithStarSizedMimeList()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");
        var document = XDocument.Parse(xaml);
        var section = Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding ShowAiDescriptionSection}");

        Assert.Equal("Grid", section.Name.LocalName);
        Assert.Equal("Auto,Auto,*,Auto", (string?)section.Attribute("RowDefinitions"));
        Assert.Null(section.Attribute("Height"));

        var listBorder = Assert.Single(section.Descendants(Avalonia + "Border"),
            border => (string?)border.Attribute(Xaml2006 + "Name") == "AiScanExclusionsListBorder");
        Assert.Null(listBorder.Attribute("Height"));

        var listHost = listBorder.Parent!;
        Assert.Equal("Grid", listHost.Name.LocalName);
        Assert.Equal("Auto,*", (string?)listHost.Attribute("RowDefinitions"));
        Assert.Equal("2", (string?)listHost.Attribute("Grid.Row"));
        Assert.Equal("1", (string?)listBorder.Attribute("Grid.Row"));

        var list = Assert.Single(listBorder.Descendants(Avalonia + "ListBox"));
        Assert.Null(list.Attribute("Height"));
        Assert.Equal("Auto", (string?)list.Attribute("ScrollViewer.VerticalScrollBarVisibility"));

        // The divider above the MIME group and the alert below it keep their place.
        var divider = Assert.Single(section.Elements(Avalonia + "Border"),
            border => (string?)border.Attribute("Grid.Row") == "1");
        Assert.Equal("1", (string?)divider.Attribute("Height"));
        var alert = Assert.Single(section.Elements(Avalonia + "Border"),
            border => (string?)border.Attribute("IsVisible") == "{Binding ShowAiDescriptionAlert}");
        Assert.Equal("3", (string?)alert.Attribute("Grid.Row"));
    }

    [Fact]
    public void ScanSection_HostsSettingsInScrollableViewerAboveTheRulesTable()
    {
        var section = GetScanSection();

        Assert.Equal("Grid", section.Name.LocalName);
        Assert.Equal("Auto,*,Auto,Auto,*,Auto", (string?)section.Attribute("RowDefinitions"));

        var viewer = Assert.Single(section.Elements(Avalonia + "ScrollViewer"));
        Assert.Equal("1", (string?)viewer.Attribute("Grid.Row"));
        Assert.Equal("ScanSettingsScrollViewer", (string?)viewer.Attribute(Xaml2006 + "Name"));
        Assert.Equal("Auto", (string?)viewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)viewer.Attribute("HorizontalScrollBarVisibility"));

        var settings = Assert.Single(viewer.Elements(Avalonia + "StackPanel"));
        var settingsMarkup = settings.ToString();
        Assert.Contains("UiKeys.ScanSettings", settingsMarkup, StringComparison.Ordinal);
        Assert.Contains("UiKeys.ScanCompletionHeader", settingsMarkup, StringComparison.Ordinal);
        Assert.Contains("UiKeys.ActivateMainWindowAfterScan", settingsMarkup, StringComparison.Ordinal);
        Assert.Contains("UiKeys.PlaySoundAfterScan", settingsMarkup, StringComparison.Ordinal);

        // The settings stack must not sit loose in the page grid again.
        Assert.DoesNotContain(section.Elements(Avalonia + "StackPanel"),
            panel => panel.ToString().Contains("UiKeys.ScanSettings", StringComparison.Ordinal));

        var rulesToolbar = Assert.Single(section.Elements(Avalonia + "Grid"),
            grid => (string?)grid.Attribute("Grid.Row") == "3");
        var toolbarMarkup = rulesToolbar.ToString();
        Assert.Contains("ResetScanExclusionRulesCommand", toolbarMarkup, StringComparison.Ordinal);
        Assert.Contains("AddScanExclusionRuleCommand", toolbarMarkup, StringComparison.Ordinal);
        Assert.Contains("RemoveScanExclusionRuleCommand", toolbarMarkup, StringComparison.Ordinal);

        var rulesBorder = Assert.Single(section.Elements(Avalonia + "Border"),
            border => (string?)border.Attribute(Xaml2006 + "Name") == "ScanExclusionRulesBorder");
        Assert.Equal("4", (string?)rulesBorder.Attribute("Grid.Row"));
        var rulesHost = Assert.Single(rulesBorder.Elements(Avalonia + "Grid"));
        Assert.Equal("Auto,*", (string?)rulesHost.Attribute("RowDefinitions"));

        var rulesList = Assert.Single(rulesHost.Descendants(Avalonia + "ListBox"));
        Assert.Equal("1", (string?)rulesList.Attribute("Grid.Row"));
        Assert.Equal("ScanExclusionRulesList", (string?)rulesList.Attribute(Xaml2006 + "Name"));
        Assert.Equal("Auto", (string?)rulesList.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
    }

    [Fact]
    public void RootGrid_KeepsSaveCancelFooterOutOfTheContentRow()
    {
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml"));
        var rootGrid = Assert.Single(document.Root!.Elements(Avalonia + "Grid"));

        Assert.Equal("Auto,*,Auto", (string?)rootGrid.Attribute("RowDefinitions"));

        var footer = Assert.Single(rootGrid.Elements(Avalonia + "Border"),
            border => (string?)border.Attribute("Grid.Row") == "2");
        Assert.Contains("ConfirmCommand", footer.ToString(), StringComparison.Ordinal);
        Assert.Contains("OnCancelClicked", footer.ToString(), StringComparison.Ordinal);

        var contentGrid = Assert.Single(rootGrid.Elements(Avalonia + "Grid"),
            grid => (string?)grid.Attribute("Grid.Row") == "1");
        Assert.Contains(contentGrid.Descendants(Avalonia + "ScrollViewer"),
            viewer => (string?)viewer.Attribute(Xaml2006 + "Name") == "ScanSettingsScrollViewer");
    }

    private static XElement GetScanSection()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");
        var document = XDocument.Parse(xaml);

        return Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding ShowScanSection}");
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
