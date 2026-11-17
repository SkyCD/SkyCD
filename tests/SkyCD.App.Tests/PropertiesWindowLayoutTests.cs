using System;
using System.IO;
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
    public void Dialog_IsResizableWithoutFixedMaximum()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");

        Assert.Contains("CanResize=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"411\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxHeight=\"426\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"411\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"426\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_PreservesTabsAndActions()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");

        Assert.Contains("<TabControl", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{i18n:Translate {x:Static i18n:UiKeys.General}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{i18n:Translate {x:Static i18n:UiKeys.Properties}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"{i18n:Translate {x:Static i18n:UiKeys.CustomProperties}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Name}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("RtfText=\"{Binding Comments, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding ConfirmCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnCancelClicked\"", xaml, StringComparison.Ordinal);
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
