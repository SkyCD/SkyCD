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
    public void SidebarAndBreadcrumb_WrapLongCategoryNames()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");
        var document = XDocument.Parse(xaml);

        var sidebarItem = Assert.Single(document.Descendants(Avalonia + "TextBlock"),
            element => element.Attribute("Classes.sidebarGroup") is not null);
        Assert.Equal("Wrap", (string?)sidebarItem.Attribute("TextWrapping"));

        var breadcrumb = Assert.Single(document.Descendants(Avalonia + "TextBlock"),
            element => element.Descendants(Avalonia + "Run")
                .Any(run => (string?)run.Attribute("Text") == "{Binding CurrentCategoryPath}"));
        Assert.Equal("Wrap", (string?)breadcrumb.Attribute("TextWrapping"));
    }

    [Fact]
    public void CustomPropertiesSection_UsesFullHeightStatusesGridLayout()
    {
        var section = GetCustomPropertiesSection();

        Assert.Equal("Grid", section.Name.LocalName);
        Assert.Equal("Auto,*,Auto", (string?)section.Attribute("RowDefinitions"));
        Assert.Equal("{DynamicResource SkySpaceSm}", (string?)section.Attribute("RowSpacing"));
    }

    [Fact]
    public void CustomPropertiesSection_StartsWithDescriptionAndRightAlignedToolbar()
    {
        // Issue #1213: the page title duplicated the breadcrumb, so the section now
        // starts with the description row and only the table keeps a divider.
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
        Assert.DoesNotContain(section.Elements(Avalonia + "Border"),
            border => (string?)border.Attribute("Height") == "1" && (string?)border.Attribute("Background") == "{DynamicResource ThemeBorderLowBrush}");
    }

    [Fact]
    public void CustomPropertiesSection_UsesBorderedTableWithSemiBoldColumnHeaders()
    {
        var section = GetCustomPropertiesSection();
        var table = Assert.Single(section.Descendants(Avalonia + "Border"),
            border => (string?)border.Attribute("BorderThickness") == "1" && (string?)border.Attribute("BorderBrush") == "{DynamicResource ThemeBorderLowBrush}");
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
        Assert.Equal("{DynamicResource SkyRowPadding}", (string?)row.Attribute("Padding"));
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
        Assert.Equal("Auto,*", (string?)section.Attribute("RowDefinitions"));
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
    public void ToolbarSection_ShowsACommandIconInBothLists()
    {
        // Issue #933: commands are recognizable before they are added, so both lists carry the icon column.
        var section = GetToolbarSection();
        var lists = section.Descendants(Avalonia + "ListBox").ToArray();
        Assert.Equal(2, lists.Length);

        foreach (var list in lists)
        {
            var template = Assert.Single(list.Descendants(Avalonia + "DataTemplate"));
            var icon = Assert.Single(template.Descendants(Avalonia + "Image"));
            Assert.Contains("IconPath", (string?)icon.Attribute("Source"), StringComparison.Ordinal);
            Assert.Contains("ToolbarIconConverter", (string?)icon.Attribute("Source"), StringComparison.Ordinal);
            Assert.Equal("{DynamicResource SkyIconSize}", (string?)icon.Attribute("Width"));
            Assert.Equal("{DynamicResource SkyIconSize}", (string?)icon.Attribute("Height"));

            // The icon owns the first column; the label (or the separator line) sits beside it in the second.
            var row = Assert.IsType<XElement>(icon.Parent);
            Assert.Equal("Grid", row.Name.LocalName);
            Assert.Equal("Auto,*", (string?)row.Attribute("ColumnDefinitions"));
            Assert.Null(icon.Attribute("Grid.Column"));
            string ColumnOf(XElement element) => element.AncestorsAndSelf()
                .Select(node => (string?)node.Attribute("Grid.Column"))
                .First(column => column is not null)!;
            var label = row.Descendants().First(element => (string?)element.Attribute("Text") == "{Binding Label}");
            var separatorLine = row.Descendants()
                .Where(element => (string?)element.Attribute("IsVisible") == "{Binding IsSeparator}")
                .ToArray();
            Assert.Equal("1", ColumnOf(label));

            // The separator spans the whole row so the line, not the icon slot, is the plain boundary.
            Assert.All(separatorLine, element =>
            {
                Assert.Equal("0", ColumnOf(element));
                Assert.Equal("2", (string?)element.Attribute("Grid.ColumnSpan"));
            });
        }

        // The commands list shows the category under the label; the toolbar list keeps the plain separator line.
        Assert.Contains(lists[0].Descendants(Avalonia + "TextBlock"), text => (string?)text.Attribute("Text") == "{Binding Category}");
        Assert.Contains(lists[1].Descendants(Avalonia + "Border"), border => (string?)border.Attribute("Height") == "1");
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
    public void ScanSection_HostsSettingsInItsOwnScrollablePage()
    {
        var section = GetScanSection();

        Assert.Equal("Grid", section.Name.LocalName);
        Assert.Equal("*", (string?)section.Attribute("RowDefinitions"));

        var viewer = Assert.Single(section.Elements(Avalonia + "ScrollViewer"));
        Assert.Null(viewer.Attribute("Grid.Row"));
        Assert.Equal("ScanSettingsScrollViewer", (string?)viewer.Attribute(Xaml2006 + "Name"));
        Assert.Equal("Auto", (string?)viewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)viewer.Attribute("HorizontalScrollBarVisibility"));

        var settings = Assert.Single(viewer.Elements(Avalonia + "StackPanel"));
        var settingsMarkup = settings.ToString();
        Assert.Contains("UiKeys.ScanSettings", settingsMarkup, StringComparison.Ordinal);
        Assert.Contains("UiKeys.ShowScanErrorsAfterScan", settingsMarkup, StringComparison.Ordinal);
        Assert.Contains("UiKeys.SaveScanErrorsToComments", settingsMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.ScanCompletionHeader", settingsMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.EjectMediaAfterScan", settingsMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.ActivateMainWindowAfterScan", settingsMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.PlaySoundAfterScan", settingsMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetScanExclusionRulesCommand", settingsMarkup, StringComparison.Ordinal);

        Assert.DoesNotContain(section.Elements(Avalonia + "StackPanel"),
            panel => panel.ToString().Contains("UiKeys.ScanSettings", StringComparison.Ordinal));
    }

    [Fact]
    public void ScanRulesSection_HostsOnlyThePatternTableAndToolbar()
    {
        var section = GetSection("ShowScanRulesSection");

        Assert.Equal("Grid", section.Name.LocalName);
        Assert.Equal("Auto,*,Auto", (string?)section.Attribute("RowDefinitions"));

        var toolbar = Assert.Single(section.Elements(Avalonia + "Grid"),
            grid => grid.Attribute("Grid.Row") is null);
        var toolbarMarkup = toolbar.ToString();
        Assert.Contains("UiKeys.ExclusionRules", toolbarMarkup, StringComparison.Ordinal);
        Assert.Contains("ResetScanExclusionRulesCommand", toolbarMarkup, StringComparison.Ordinal);
        Assert.Contains("AddScanExclusionRuleCommand", toolbarMarkup, StringComparison.Ordinal);
        Assert.Contains("RemoveScanExclusionRuleCommand", toolbarMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.SkipSymbolicLinks", section.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.ScanCompletionHeader", section.ToString(), StringComparison.Ordinal);

        var rulesBorder = Assert.Single(section.Elements(Avalonia + "Border"),
            border => (string?)border.Attribute(Xaml2006 + "Name") == "ScanExclusionRulesBorder");
        Assert.Equal("1", (string?)rulesBorder.Attribute("Grid.Row"));
        var rulesHost = Assert.Single(rulesBorder.Elements(Avalonia + "Grid"));
        Assert.Equal("Auto,*", (string?)rulesHost.Attribute("RowDefinitions"));

        var rulesList = Assert.Single(rulesHost.Descendants(Avalonia + "ListBox"));
        Assert.Equal("1", (string?)rulesList.Attribute("Grid.Row"));
        Assert.Equal("ScanExclusionRulesList", (string?)rulesList.Attribute(Xaml2006 + "Name"));
        Assert.Equal("Auto", (string?)rulesList.Attribute("ScrollViewer.VerticalScrollBarVisibility"));

        var alert = Assert.Single(section.Elements(Avalonia + "Border"),
            border => (string?)border.Attribute("IsVisible") == "{Binding ShowScanAlert}");
        Assert.Equal("2", (string?)alert.Attribute("Grid.Row"));
    }

    [Fact]
    public void ScanCompletionSection_HostsOnlyTheFinishActions()
    {
        var section = GetSection("ShowScanCompletionSection");

        Assert.Equal("Grid", section.Name.LocalName);
        Assert.Equal("*", (string?)section.Attribute("RowDefinitions"));

        var viewer = Assert.Single(section.Elements(Avalonia + "ScrollViewer"));
        Assert.Equal("ScanCompletionScrollViewer", (string?)viewer.Attribute(Xaml2006 + "Name"));
        Assert.Equal("Auto", (string?)viewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)viewer.Attribute("HorizontalScrollBarVisibility"));

        var markup = Assert.Single(viewer.Elements(Avalonia + "StackPanel")).ToString();
        Assert.Contains("UiKeys.ScanCompletionHeader", markup, StringComparison.Ordinal);
        Assert.Contains("UiKeys.EjectMediaAfterScan", markup, StringComparison.Ordinal);
        Assert.Contains("UiKeys.ActivateMainWindowAfterScan", markup, StringComparison.Ordinal);
        Assert.Contains("UiKeys.PlaySoundAfterScan", markup, StringComparison.Ordinal);
        Assert.Contains("MediaEjectionSupportHint", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("UiKeys.SkipSymbolicLinks", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetScanExclusionRulesCommand", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ScanExclusionRules", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectionsSection_UsesSharedListLayoutWithTwoListsAndToolbarActions()
    {
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml"));
        var section = Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding ShowCollectionsSection}");

        Assert.Equal("Auto,*,Auto", (string?)section.Attribute("RowDefinitions"));
        Assert.DoesNotContain("#", section.ToString(), StringComparison.Ordinal);

        var lists = section.Descendants(Avalonia + "ListBox").ToArray();
        Assert.Equal(2, lists.Length);
        Assert.Contains(lists, list => ((string?)list.Attribute("ItemsSource"))?.Contains("CollectionsPage.Collections", StringComparison.Ordinal) == true);
        Assert.Contains(lists, list => ((string?)list.Attribute("ItemsSource"))?.Contains("CollectionsPage.FilteredMembers", StringComparison.Ordinal) == true);
        Assert.Contains(lists, list => (string?)list.Attribute("DoubleTapped") == "OnCollectionMemberDoubleTapped");

        var commands = section.Descendants(Avalonia + "Button")
            .Select(button => button.Attribute("Command")?.Value ?? string.Empty)
            .ToArray();
        Assert.Equal(
        [
            "{Binding CollectionsPage.AddCollectionCommand}",
            "{Binding CollectionsPage.DeleteCollectionCommand}",
            "{Binding CollectionsPage.ConfirmDeleteCollectionCommand}",
            "{Binding CollectionsPage.CancelDeleteCollectionCommand}",
            "{Binding $parent[Window].((vm:OptionsDialogViewModel)DataContext).CollectionsPage.CommitRenameCollectionCommand}",
            "{Binding $parent[Window].((vm:OptionsDialogViewModel)DataContext).CollectionsPage.CancelRenameCollectionCommand}",
            "{Binding $parent[Window].((vm:OptionsDialogViewModel)DataContext).CollectionsPage.BeginRenameCollectionCommand}",
            "",
            "{Binding CollectionsPage.AddSelectedEntriesCommand}",
            "{Binding CollectionsPage.RequestRemoveSelectedMemberCommand}",
            "{Binding CollectionsPage.ConfirmRemoveSelectedMemberCommand}",
            "{Binding CollectionsPage.CancelRemoveMemberCommand}"
        ], commands);

        var glyphs = section.Descendants(Avalonia + "Button")
            .Where(button => (string?)button.Attribute("Content") is "+" or "-")
            .ToArray();
        Assert.Equal(4, glyphs.Length);
        Assert.All(glyphs, glyph =>
        {
            Assert.Equal("34", (string?)glyph.Attribute("Width"));
            Assert.Equal("28", (string?)glyph.Attribute("Height"));
            Assert.NotNull(glyph.Attribute("ToolTip.Tip"));
        });

        var menuCommands = section.Descendants(Avalonia + "MenuItem")
            .Select(item => item.Attribute("Command")?.Value ?? string.Empty)
            .ToArray();
        Assert.Equal(
        [
            "{Binding CollectionsPage.RemoveMissingEntriesCommand}",
            "{Binding CollectionsPage.NavigateToSelectedCommand}",
            "{Binding CollectionsPage.OpenMemberFolderCommand}"
        ], menuCommands);
        Assert.All(section.Descendants(Avalonia + "MenuItem"),
            item => Assert.NotNull(item.Attribute("Header")));
        Assert.Single(section.Descendants(Avalonia + "TextBox"));
        Assert.DoesNotContain("NewCollectionNameBox", section.ToString(), StringComparison.Ordinal);
        var memberFilter = Assert.Single(section.Descendants(),
            element => element.Name.LocalName == "SearchTextBox");
        Assert.Equal("{Binding CollectionsPage.MemberFilterText}", (string?)memberFilter.Attribute("Text"));
        Assert.Contains(section.Descendants(Avalonia + "TextBlock"),
            block => (string?)block.Attribute("Text") == "{i18n:Translate {x:Static i18n:UiKeys.NoCollections}}");
    }

    [Fact]
    public void RootGrid_KeepsSaveCancelFooterOutOfTheContentRow()
    {
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml"));
        var rootGrid = Assert.Single(document.Root!.Elements(Avalonia + "Grid"));

        Assert.Equal("*,Auto", (string?)rootGrid.Attribute("RowDefinitions"));

        var footer = Assert.Single(rootGrid.Elements(Avalonia + "Border"),
            border => (string?)border.Attribute("Grid.Row") == "1");
        Assert.Contains("ConfirmCommand", footer.ToString(), StringComparison.Ordinal);
        Assert.Contains("OnCancelClicked", footer.ToString(), StringComparison.Ordinal);

        var contentGrid = Assert.Single(rootGrid.Elements(Avalonia + "Grid"));
        Assert.Contains(contentGrid.Descendants(Avalonia + "ScrollViewer"),
            viewer => (string?)viewer.Attribute(Xaml2006 + "Name") == "ScanSettingsScrollViewer");
    }

    private static XElement GetScanSection() => GetSection("ShowScanSection");

    private static XElement GetSection(string visibilityFlag)
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "OptionsWindow.axaml");
        var document = XDocument.Parse(xaml);

        return Assert.Single(document.Descendants(),
            element => (string?)element.Attribute("IsVisible") == "{Binding " + visibilityFlag + "}");
    }

    [Fact]
    public void CollectionsWindow_IsNoLongerASeparateDialog()
    {
        var root = FindRepoRoot();

        Assert.False(File.Exists(Path.Combine(root, "src", "SkyCD.App", "Views", "CollectionsWindow.axaml")));
        Assert.False(File.Exists(Path.Combine(root, "src", "SkyCD.App", "Views", "CollectionsWindow.axaml.cs")));
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
