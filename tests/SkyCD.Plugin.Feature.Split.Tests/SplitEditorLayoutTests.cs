using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.Plugin.Feature.Split.Tests;

/// <summary>
/// Issue #1310: the split form is plugin content embedded in a host-owned window. The host sizes and closes
/// the window; the form only lays out its options, the format picker and the progress row, so it never grows
/// or clips while a split runs.
/// </summary>
public sealed class SplitEditorLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void TheFormIsEmbeddableContentNotAWindow()
    {
        var root = ReadSplitEditor();

        Assert.Equal(Avalonia + "UserControl", root.Name);
        Assert.Null(root.Attribute("Width"));
        Assert.Null(root.Attribute("Height"));
    }

    [Fact]
    public void TheForm_DoesNotDeclareLiteralInsets()
    {
        var xaml = ReadSplitEditor().ToString();

        Assert.DoesNotContain("Margin=\"0,", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Margin=\"8", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Padding=\"0,", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatPicker_ShowsFormatDisplayNames()
    {
        var picker = ReadSplitEditor()
            .Descendants(Avalonia + "ComboBox")
            .Single(box => ((string?)box.Attribute("SelectedItem"))?.Contains("SelectedSplitFormat",
                StringComparison.Ordinal) == true);

        Assert.NotNull(picker.Element(Avalonia + "ComboBox.ItemTemplate"));
        Assert.Contains("DisplayName", picker.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Actions_OfferSplitCancelAndClose()
    {
        var buttons = ReadSplitEditor().Descendants(Avalonia + "Button").ToArray();

        var split = Assert.Single(buttons, button =>
            (string?)button.Attribute("Command") == "{Binding SplitCommand}");
        Assert.Equal("True", split.Attribute("IsDefault")?.Value);

        Assert.Single(buttons, button =>
            (string?)button.Attribute("Command") == "{Binding CancelCommand}");
        Assert.Single(buttons, button =>
            (string?)button.Attribute("IsCancel") == "True");
    }

    private static XElement ReadSplitEditor()
    {
        var path = Path.Combine(FindRepoRoot(), "Plugins", "SkyCD.Plugin.Feature.Split", "Views",
            "SplitEditor.axaml");
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
