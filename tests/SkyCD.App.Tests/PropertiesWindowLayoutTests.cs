using System;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

public class PropertiesWindowLayoutTests
{
    [Fact]
    public void GeneralTab_IconSpansNameLabelAndField()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");

        Assert.Contains("Grid.RowSpan=\"2\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneralTab_IconSizesFromNameFields()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");

        Assert.Contains("#NameFields.Bounds.Height", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Height=\"32\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Stretch", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_IsFixedSizeAndCenteredOnOwner()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");
        var window = XDocument.Parse(xaml).Root!;

        Assert.Equal("411", (string?)window.Attribute("Width"));
        Assert.Equal("560", (string?)window.Attribute("Height"));
        Assert.Equal("False", (string?)window.Attribute("CanResize"));
        Assert.Equal("False", (string?)window.Attribute("CanMinimize"));
        Assert.Equal("CenterOwner", (string?)window.Attribute("WindowStartupLocation"));
        Assert.Null(window.Attribute("MinWidth"));
        Assert.Null(window.Attribute("MinHeight"));
    }

    [Fact]
    public void Dialog_PreservesTabsAndActions()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");

        Assert.Contains("<TabControl", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{i18n:Translate {x:Static i18n:UiKeys.General}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{i18n:Translate {x:Static i18n:UiKeys.Properties}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{i18n:Translate {x:Static i18n:UiKeys.Tags}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{i18n:Translate {x:Static i18n:UiKeys.CustomProperties}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RtfText=\"{Binding Comments, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ConfirmCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnCancelClicked\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomProperties_UsesSharedTableAndEditableValueTemplate()
    {
        var window = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");
        var table = ReadRepoFile("src", "SkyCD.UI", "Controls", "Properties", "PropertiesList.axaml");

        Assert.Contains("Rows=\"{Binding CustomProperties}\"", window, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasCustomProperties}\"", window, StringComparison.Ordinal);
        Assert.Contains("<cp:PropertiesList.PropertyCellTemplate>", window, StringComparison.Ordinal);
        Assert.Contains("<cp:PropertiesList.ValueCellTemplate>", window, StringComparison.Ordinal);
        Assert.Contains("EditValueInPlace=\"True\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding DefaultValue}\"", window, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsEditing}\"", window, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding EditValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}\"", window, StringComparison.Ordinal);
        Assert.Contains("Classes=\"value-cell\"", table, StringComparison.Ordinal);
        Assert.Contains("PropertyHeader=\"{i18n:Translate {x:Static i18n:UiKeys.Property}}\"", window, StringComparison.Ordinal);
        Assert.Contains("ValueHeader=\"{i18n:Translate {x:Static i18n:UiKeys.Value}}\"", window, StringComparison.Ordinal);
        Assert.Contains("ContentTemplate=\"{Binding PropertyCellTemplate, ElementName=Root}\"", table, StringComparison.Ordinal);
        Assert.Contains("ContentTemplate=\"{Binding ValueCellTemplate, ElementName=Root}\"", table, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", table, StringComparison.Ordinal);
    }

    [Fact]
    public void TagsTab_ContentsAreScrollable()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");
        XNamespace avalonia = "https://github.com/avaloniaui";
        var tab = Assert.Single(XDocument.Parse(xaml).Descendants(avalonia + "TabItem"),
            item => ((string?)item.Attribute("Header"))?.Contains("UiKeys.Tags}}", StringComparison.Ordinal) == true);
        var scrollViewer = Assert.Single(tab.Elements(avalonia + "ScrollViewer"));

        Assert.Contains("{Binding TagAssignments}", scrollViewer.ToString(), StringComparison.Ordinal);
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
