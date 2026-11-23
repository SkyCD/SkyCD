using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1003: menu rows take their padding, height and column widths from design tokens via one
/// shared MenuItem theme, so the menu bar and every context menu line up on the same gutter,
/// label, hotkey and submenu columns.
/// </summary>
public sealed class MenuThemeLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Regex NumericRowMetric = new(
        "(Padding|MinHeight|Width|Height|Margin)=\"[0-9.]",
        RegexOptions.Compiled);

    private static readonly Regex NumericRowMetricSetter = new(
        "Property=\"(Padding|MinHeight|Width|Height|Margin)\" Value=\"[0-9.]",
        RegexOptions.Compiled);

    private static readonly Regex TokenReference = new(
        "DynamicResource (Sky[A-Za-z]+)",
        RegexOptions.Compiled);

    [Fact]
    public void App_IncludesSharedMenuStyles()
    {
        var app = ReadRepoFile("src", "SkyCD.App", "App.axaml");

        Assert.Contains(
            "<StyleInclude Source=\"avares://SkyCD.App/Styles/MenuStyles.axaml\" />",
            app,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MenuTheme_HasNoLiteralRowMetricsAndOnlyTokenBrushes()
    {
        var styles = ReadRepoFile("src", "SkyCD.App", "Styles", "MenuStyles.axaml");

        Assert.DoesNotMatch(NumericRowMetric, styles);
        Assert.DoesNotMatch(NumericRowMetricSetter, styles);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6,8}", styles);

        var defined = TokenDefinitionKeys();
        var missing = TokenReference.Matches(styles)
            .Select(match => match.Groups[1].Value)
            .Where(key => !defined.Contains(key))
            .Distinct()
            .ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void DesignTokens_DefineTheMenuRhythm()
    {
        var tokens = ReadRepoFile("src", "SkyCD.App", "Styles", "DesignTokens.axaml");

        Assert.Contains("x:Key=\"SkyMenuItemMinHeight\"", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SkyMenuItemPadding\"", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SkyMenuGutterWidth\"", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SkyMenuIconMargin\"", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SkyMenuArrowColumnWidth\"", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SkyMenuSeparatorHeight\"", tokens, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SkyMenuSeparatorRowHeight\"", tokens, StringComparison.Ordinal);
    }

    [Fact]
    public void MenuTheme_ReservesFixedGutterLabelGestureAndArrowColumns()
    {
        var theme = AppMenuItemTheme();
        var template = TemplateOf(theme);

        var columns = template.Descendants(Avalonia + "Grid").First();
        Assert.Equal("Auto,*,Auto,Auto", (string?)columns.Attribute("ColumnDefinitions"));

        var gutter = Single(template, "Panel", "IconGutter");
        Assert.Equal("0", (string?)gutter.Attribute("Grid.Column"));
        Assert.Equal("{DynamicResource SkyMenuGutterWidth}", (string?)gutter.Attribute("Width"));
        Assert.NotNull(gutter.Elements(Avalonia + "ContentControl").Single(e => (string?)e.Attribute("Name") == "PART_ToggleIconPresenter"));
        var icon = gutter.Elements(Avalonia + "ContentPresenter").Single(e => (string?)e.Attribute("Name") == "PART_IconPresenter");
        Assert.Equal("{TemplateBinding Icon}", (string?)icon.Attribute("Content"));

        var header = Single(template, "ContentPresenter", "PART_HeaderPresenter");
        Assert.Equal("1", (string?)header.Attribute("Grid.Column"));
        Assert.Equal("{TemplateBinding Padding}", (string?)header.Attribute("Margin"));
        Assert.Equal("{TemplateBinding Header}", (string?)header.Attribute("Content"));

        var gesture = Single(template, "TextBlock", "PART_InputGestureText");
        Assert.Equal("2", (string?)gesture.Attribute("Grid.Column"));
        Assert.Equal("Right", (string?)gesture.Attribute("TextAlignment"));
        Assert.Contains(
            "Converter={StaticResource SkyMenuGestureTextConverter}",
            (string?)gesture.Attribute("Text"),
            StringComparison.Ordinal);

        var arrowPanel = Single(template, "Path", "rightArrow").Parent!;
        Assert.Equal("3", (string?)arrowPanel.Attribute("Grid.Column"));
        Assert.Equal("{DynamicResource SkyMenuArrowColumnWidth}", (string?)arrowPanel.Attribute("Width"));

        var popup = Single(template, "Popup", "PART_Popup");
        Assert.Equal("RightEdgeAlignedTop", (string?)popup.Attribute("Placement"));
        var presenter = popup.Descendants(Avalonia + "ItemsPresenter").Single(e => (string?)e.Attribute("Name") == "PART_ItemsPresenter");
        Assert.Equal("{DynamicResource SkyInsetXxs}", (string?)presenter.Attribute("Margin"));
    }

    [Fact]
    public void MenuTheme_PreservesViewModelBindingsAndRecursiveItemTheme()
    {
        var theme = AppMenuItemTheme();
        var setters = theme.Elements(Avalonia + "Setter")
            .Where(setter => (string?)setter.Attribute("Value") is string value && value.StartsWith("{ReflectionBinding DataContext.", StringComparison.Ordinal))
            .ToDictionary(
                setter => (string)setter.Attribute("Property")!,
                setter => (string)setter.Attribute("Value")!);

        var expectedPaths = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Header"] = "DataContext.Header",
            ["HotKey"] = "DataContext.HotKey",
            ["InputGesture"] = "DataContext.InputGesture",
            ["Icon"] = "DataContext.Icon",
            ["Command"] = "DataContext.Command",
            ["CommandParameter"] = "DataContext.CommandParameter",
            ["ToggleType"] = "DataContext.ToggleType",
            ["IsChecked"] = "DataContext.IsChecked",
            ["IsEnabled"] = "DataContext.IsEnabled",
            ["ItemsSource"] = "DataContext.Items",
        };

        Assert.Equal(expectedPaths.Count, setters.Count);
        foreach (var (property, path) in expectedPaths)
        {
            Assert.Contains(path, setters[property], StringComparison.Ordinal);
        }

        var recursive = theme.Elements(Avalonia + "Setter")
            .Single(setter => (string?)setter.Attribute("Property") == "ItemContainerTheme");
        Assert.Equal("{DynamicResource AppMenuItemTheme}", (string?)recursive.Attribute("Value"));
    }

    [Fact]
    public void Separator_IsAnInsetLineInTheReservedColumnsNotADashRow()
    {
        var theme = AppMenuItemTheme();
        var separatorStyle = theme.Elements(Avalonia + "Style")
            .Single(style => (string?)style.Attribute("Selector") == "^:separator");

        var minHeight = separatorStyle.Elements(Avalonia + "Setter")
            .Single(setter => (string?)setter.Attribute("Property") == "MinHeight");
        Assert.Equal("{DynamicResource SkyMenuSeparatorRowHeight}", (string?)minHeight.Attribute("Value"));

        // Focusable stays with the framework's "-" header handling so keyboard navigation
        // skips the row; the theme must not change it.
        Assert.DoesNotContain(
            separatorStyle.Elements(Avalonia + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Focusable");

        var template = separatorStyle.Descendants(Avalonia + "ControlTemplate").Single();
        Assert.Equal("Auto,*,Auto", (string?)template.Descendants(Avalonia + "Grid").First().Attribute("ColumnDefinitions"));
        Assert.DoesNotContain(template.Descendants(Avalonia + "TextBlock"), text => (string?)text.Attribute("Text") == "-");

        var line = template.Descendants(Avalonia + "Border").Single();
        Assert.Equal("{DynamicResource ThemeControlMidBrush}", (string?)line.Attribute("Background"));
        Assert.Equal("{DynamicResource SkyMenuSeparatorHeight}", (string?)line.Attribute("Height"));
        Assert.Equal("{DynamicResource SkyMenuItemPadding}", (string?)line.Attribute("Margin"));
        Assert.Equal("1", (string?)line.Attribute("Grid.Column"));
    }

    [Fact]
    public void MainWindow_MenuBarAndContextMenusShareTheTheme()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");
        var styles = ReadRepoFile("src", "SkyCD.App", "Styles", "MenuStyles.axaml");

        var themedTopLevel = Regex.Matches(xaml, "<MenuItem[^>]*ItemContainerTheme=\"\\{StaticResource AppMenuItemTheme\\}\"", RegexOptions.Singleline);
        Assert.Equal(5, themedTopLevel.Count);
        Assert.DoesNotContain("DynamicMenuItemTheme", xaml, StringComparison.Ordinal);

        // Menu bar headers and every context menu take their row metrics from the same theme.
        Assert.Contains("<Style Selector=\"Menu > MenuItem\">", styles, StringComparison.Ordinal);
        Assert.Contains("<Style Selector=\"ContextMenu\">", styles, StringComparison.Ordinal);
        Assert.Contains(
            "<Setter Property=\"ItemContainerTheme\" Value=\"{StaticResource AppMenuItemTheme}\" />",
            styles,
            StringComparison.Ordinal);

        var contextMenus = Regex.Matches(xaml, "<ContextMenu");
        Assert.True(contextMenus.Count >= 3, $"Expected menu bar plus context menus, found {contextMenus.Count}.");
    }

    [Fact]
    public void Separators_AreStillDashHeadersInTheViewModel()
    {
        var viewModel = ReadRepoFile("src", "SkyCD.Presentation", "ViewModels", "MainWindowViewModel.cs");

        // The framework turns a "-" header into a real separator (non-focusable, not hit-testable),
        // so the separators and keyboard navigation must keep flowing through this contract.
        Assert.Contains("new MainMenuItemViewModel { Header = \"-\" }", viewModel, StringComparison.Ordinal);
        Assert.True(
            Regex.Matches(viewModel, "Separator\\(\\)").Count >= 5,
            "Expected separators to be produced by the shared Separator() helper.");
    }

    private static XElement AppMenuItemTheme()
    {
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Styles", "MenuStyles.axaml"));
        return document.Descendants(Avalonia + "ControlTheme")
            .Single(theme => (string?)theme.Attribute(Xaml + "Key") == "AppMenuItemTheme");
    }

    private static XElement TemplateOf(XElement theme) =>
        theme.Elements(Avalonia + "Setter")
            .Single(setter => (string?)setter.Attribute("Property") == "Template")
            .Descendants(Avalonia + "ControlTemplate")
            .Single();

    private static XElement Single(XElement scope, string elementName, string name) =>
        scope.Descendants(Avalonia + elementName).Single(e => (string?)e.Attribute("Name") == name);

    private static HashSet<string> TokenDefinitionKeys() =>
        Regex.Matches(ReadRepoFile("src", "SkyCD.App", "Styles", "DesignTokens.axaml"), "x:Key=\"(Sky[A-Za-z]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

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
