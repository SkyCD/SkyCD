using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #942: the Add To List dialog must give every section its own sizing row. The source panel
/// previously lived in a fixed 6 px row and drew over the name/Location block, and the overflowing
/// content pushed the validation line out of the fixed 445x480 window.
/// Issue #1150: the source panel owns the content column's star row, so the Internet form uses the
/// full remaining height instead of stopping at a fixed cap.
/// </summary>
public class AddToListWindowLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace IconPacks = "https://github.com/MahApps/IconPacks.Avalonia";
    private static readonly XNamespace Xaml2006 = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ContentColumn_HasNoFixedPixelSpacerRow()
    {
        var column = GetContentColumn();

        Assert.DoesNotContain(((string?)column.Attribute("RowDefinitions") ?? string.Empty).Split(','),
            definition => double.TryParse(definition, out _));
        Assert.Equal("Auto,*,Auto", (string?)column.Attribute("RowDefinitions"));
    }

    [Fact]
    public void EachSection_OwnsASizingRowInsideTheContentColumn()
    {
        var column = GetContentColumn();

        var nameBlock = GetNameBlock(column);
        var sourceRegion = GetSourceRegion(column);
        var destinationSection = GetDestinationSection(column);

        // The name/Location block, the source panel and the destination choices each own a row.
        Assert.Equal("0", GridRowOf(nameBlock));
        Assert.Equal("1", GridRowOf(sourceRegion));
        Assert.Equal("2", (string?)destinationSection.Attribute("Grid.Row"));
        Assert.Contains("UiKeys.SelectMediaName", nameBlock.ToString(), StringComparison.Ordinal);
        Assert.Contains("UiKeys.PhysicalLocation", nameBlock.ToString(), StringComparison.Ordinal);
        Assert.Contains(destinationSection.Descendants(Avalonia + "Button"),
            button => (string?)button.Attribute("Command") == "{Binding ConfirmCommand}");
    }

    [Fact]
    public void ContentColumn_KeepsTheActionsAnchoredToTheBottom()
    {
        var column = GetContentColumn();

        // The star row between the fields and the action row absorbs the fixed window height, so the
        // actions stay pinned to the bottom exactly as #823/#826 established.
        Assert.Equal("Auto,*,Auto", (string?)column.Attribute("RowDefinitions"));
        Assert.Equal("2", (string?)GetDestinationSection(column).Attribute("Grid.Row"));
        Assert.Single(column.Elements(Avalonia + "StackPanel"),
            panel => (string?)panel.Attribute(Xaml2006 + "Name") == "NameLocationBlock");
    }

    [Fact]
    public void DestinationRadios_RenderDirectlyWithoutATabStrip()
    {
        var column = GetContentColumn();
        var destinationSection = GetDestinationSection(column);

        Assert.DoesNotContain(column.Descendants(Avalonia + "TabControl"), _ => true);
        Assert.DoesNotContain(column.Descendants(Avalonia + "TabItem"), _ => true);
        Assert.DoesNotContain(column.Elements(Avalonia + "Grid"),
            grid => (string?)grid.Attribute(Xaml2006 + "Name") == "SourceTabHost");

        var radios = destinationSection.Descendants(Avalonia + "RadioButton").ToList();
        Assert.Equal(2, radios.Count);
        Assert.Contains(radios, radio => (string?)radio.Attribute("CommandParameter") == "NewMedia");
        Assert.Contains(radios, radio => (string?)radio.Attribute("CommandParameter") == "SelectedFolder");
    }

    [Fact]
    public void SourcePanel_FillsTheStarRowAndScrollsInsideItsBorder()
    {
        var column = GetContentColumn();
        var viewer = GetSourceRegion(column);

        Assert.Equal("ScrollViewer", viewer.Name.LocalName);
        Assert.Equal("1", (string?)viewer.Attribute("Grid.Row"));
        Assert.Null(viewer.Attribute("MaxHeight"));
        Assert.Equal("Auto", (string?)viewer.Attribute("VerticalScrollBarVisibility"));
        Assert.Equal("Disabled", (string?)viewer.Attribute("HorizontalScrollBarVisibility"));

        // Every source-specific tab keeps its own layout inside the scrolling region.
        var details = Assert.Single(viewer.Elements(Avalonia + "Grid"));
        Assert.Equal("Top", (string?)details.Attribute("VerticalAlignment"));

        foreach (var source in new[]
                 {
                     "{Binding IsSourceMedia}",
                     "{Binding IsSourceFolder}",
                     "{Binding IsSourceInternet}",
                     "{Binding IsSourceTitleOnly}",
                     "{Binding IsSourceFromAccount}"
                 })
        {
            Assert.Contains(details.Elements(),
                element => (string?)element.Attribute("IsVisible") == source);
        }

        // The Location picker stays outside the scrolling region, above it.
        Assert.DoesNotContain(viewer.Descendants(Avalonia + "ComboBox"),
            comboBox => comboBox.Attribute("SelectedItem")?.Value == "{Binding SelectedLocation}");
    }

    [Fact]
    public void ValidationMessages_RenderInlineBesideTheirFields()
    {
        // Issue #1313: errors moved from a shared window footer to the field that causes them.
        var root = GetRootGrid();
        var contentColumn = Assert.Single(root.Elements(Avalonia + "Grid"),
            grid => (string?)grid.Attribute(Xaml2006 + "Name") == "ContentColumn");
        var nameBlock = Assert.Single(contentColumn.Elements(Avalonia + "StackPanel"),
            panel => (string?)panel.Attribute(Xaml2006 + "Name") == "NameLocationBlock");
        var sourceRegion = Assert.Single(contentColumn.Elements(Avalonia + "ScrollViewer"));

        Assert.Null(root.Attribute("RowDefinitions"));
        Assert.DoesNotContain(root.Elements(), element => element.Name == Avalonia + "TextBlock");
        Assert.DoesNotContain(root.Descendants(Avalonia + "TextBlock"),
            textBlock => (string?)textBlock.Attribute(Xaml2006 + "Name") == "ValidationMessageText");

        var fieldHosts = new (XElement Host, string Binding)[]
        {
            (nameBlock, "MediaNameValidationMessage"),
            (sourceRegion, "DriveValidationMessage"),
            (sourceRegion, "FolderPathValidationMessage"),
            (sourceRegion, "InternetAddressValidationMessage"),
            (sourceRegion, "InternetPathValidationMessage"),
            (sourceRegion, "InternetCredentialsValidationMessage"),
            (sourceRegion, "AccountValidationMessage"),
            (sourceRegion, "CaptureTypeValidationMessage"),
            (sourceRegion, "CaptureLocationValidationMessage"),
            (sourceRegion, "CaptureDestinationValidationMessage"),
        };

        foreach (var (host, binding) in fieldHosts)
        {
            Assert.Contains(host.Descendants(Avalonia + "TextBlock"),
                textBlock => (string?)textBlock.Attribute("Text") == $"{{Binding {binding}}}");
        }

        var errorBlocks = root.Descendants(Avalonia + "TextBlock")
            .Where(textBlock => ((string?)textBlock.Attribute("Text"))
                ?.EndsWith("ValidationMessage}", StringComparison.Ordinal) == true)
            .ToList();
        Assert.True(errorBlocks.Count >= fieldHosts.Length,
            "every failing field needs its own inline message block.");
        Assert.All(errorBlocks, textBlock =>
        {
            Assert.Equal("{DynamicResource SkyErrorForeground}", (string?)textBlock.Attribute("Foreground"));
            Assert.Equal("Wrap", (string?)textBlock.Attribute("TextWrapping"));
            Assert.Contains("IsNotNullOrEmpty", (string?)textBlock.Attribute("IsVisible"), StringComparison.Ordinal);
        });

        // The visible text is announced through the focusable field as well.
        Assert.Contains(nameBlock.Descendants(Avalonia + "TextBox"),
            textBox => (string?)textBox.Attribute("AutomationProperties.HelpText")
                == "{Binding MediaNameValidationMessage}");
        Assert.Contains(sourceRegion.Descendants(Avalonia + "TextBox"),
            textBox => (string?)textBox.Attribute("AutomationProperties.HelpText")
                == "{Binding InternetAddressValidationMessage}");
    }

    [Fact]
    public void SourceSelector_UsesDistinctColoredArtwork()
    {
        // Issue #1099: every source mode shows its own colored artwork from the shared Twemoji pipeline,
        // instead of the monochrome line glyphs introduced with #1061.
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "AddToListWindow.axaml"));
        var buttons = document.Descendants(Avalonia + "Button")
            .Where(button => ((string?)button.Attribute("Classes") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("source"))
            .ToList();

        Assert.Equal(6, buttons.Count);
        Assert.DoesNotContain(document.Descendants(IconPacks + "PackIconControl"),
            icon => icon.Ancestors(Avalonia + "Button")
                .Any(button => ((string?)button.Attribute("Classes") ?? string.Empty).Contains("source")));

        var images = buttons
            .Select(button => Assert.Single(button.Descendants(Avalonia + "Image")))
            .ToList();
        var sources = images.Select(image => (string?)image.Attribute("Source")).ToList();

        Assert.Equal(
            ["{x:Static icons:SourceIcons.Media}",
             "{x:Static icons:SourceIcons.Folder}",
             "{x:Static icons:SourceIcons.Internet}",
             "{x:Static icons:SourceIcons.TitleOnly}",
             "{x:Static icons:SourceIcons.Account}",
             "{x:Static icons:SourceIcons.Capture}"],
            sources);
        Assert.All(images, image =>
        {
            Assert.Equal("40", (string?)image.Attribute("Width"));
            Assert.Equal("40", (string?)image.Attribute("Height"));
        });
    }

    [Fact]
    public void Layout_UsesDesignTokensForSpacing()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "AddToListWindow.axaml");

        foreach (var token in new[]
                 {
                     "{DynamicResource SkySpaceXs}",
                     "{DynamicResource SkySpaceSm}",
                     "{DynamicResource SkyInsetXxs}",
                     "{DynamicResource SkyWindowPadding}"
                 })
        {
            Assert.Contains(token, xaml, StringComparison.Ordinal);
        }
    }

    private static XElement GetContentColumn() => Assert.Single(GetRootGrid().Elements(Avalonia + "Grid"),
        grid => (string?)grid.Attribute(Xaml2006 + "Name") == "ContentColumn");

    private static XElement GetDestinationSection(XElement column) =>
        Assert.Single(column.Elements(Avalonia + "Grid"),
            grid => (string?)grid.Attribute(Xaml2006 + "Name") == "DestinationSection");

    private static XElement GetNameBlock(XElement column) =>
        Assert.Single(column.Elements(Avalonia + "StackPanel"),
            panel => (string?)panel.Attribute(Xaml2006 + "Name") == "NameLocationBlock");

    private static XElement GetSourceRegion(XElement column) =>
        Assert.Single(column.Elements(Avalonia + "ScrollViewer"));

    private static XElement GetRootGrid()
    {
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "AddToListWindow.axaml"));
        return Assert.Single(document.Root!.Elements(Avalonia + "Grid"));
    }

    private static string? GridRowOf(XElement element) => (string?)element.Attribute("Grid.Row") ?? "0";

    private static string ReadRepoFile(params string[] parts)
    {
        var fullPath = Path.Combine([FindRepoRoot(), .. parts]);
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
