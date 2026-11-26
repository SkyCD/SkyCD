using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1183: File &gt; Split... keeps one fixed size with the part options, the saving format picker
/// and the progress row, so the form never moves or clips while a split runs.
/// </summary>
public sealed class SplitWindowLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void Window_KeepsOneFixedSize()
    {
        var window = ReadSplitWindow();

        Assert.Equal("620", window.Attribute("Width")?.Value);
        Assert.Equal("560", window.Attribute("Height")?.Value);
        Assert.Equal("False", window.Attribute("CanResize")?.Value);
        Assert.Null(window.Attribute("SizeToContent"));
    }

    [Fact]
    public void Window_DoesNotDeclareLiteralInsets()
    {
        var xaml = ReadSplitWindow().ToString();

        Assert.DoesNotContain("Margin=\"0,", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"8", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Padding=\"0,", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPicker_ShowsFormatDisplayNames()
    {
        var picker = ReadSplitWindow()
            .Descendants(Avalonia + "ComboBox")
            .Single(box => (string?)box.Attribute("AutomationProperties.Name") is string name &&
                name.Contains("SplitSavingFormat"));

        Assert.NotNull(picker.Element(Avalonia + "ComboBox.ItemTemplate"));
        Assert.Contains("DisplayName", picker.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Actions_OfferSplitCancelAndClose()
    {
        var buttons = ReadSplitWindow().Descendants(Avalonia + "Button").ToArray();

        var split = Assert.Single(buttons, button =>
            (string?)button.Attribute("Command") == "{Binding SplitCommand}");
        Assert.Equal("True", split.Attribute("IsDefault")?.Value);

        Assert.Single(buttons, button =>
            (string?)button.Attribute("Command") == "{Binding CancelCommand}");
        Assert.Single(buttons, button =>
            (string?)button.Attribute("IsCancel") == "True");
    }

    private static XElement ReadSplitWindow()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Views", "SplitWindow.axaml");
        return XElement.Load(path);
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
