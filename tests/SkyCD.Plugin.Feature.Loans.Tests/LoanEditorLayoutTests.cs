using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.Plugin.Feature.Loans.Tests;

/// <summary>
/// Issue #1106: the Loan history list must fill the remaining height of the tab instead of stopping
/// at a fixed 160 px cap. A root ScrollViewer measures its content with infinite height, so a
/// star-sized row inside it can never grow to the bottom edge; the fill layout must be the root.
/// </summary>
public sealed class LoanEditorLayoutTests
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";

    [Fact]
    public void Root_IsAFillGrid_InsteadOfAScrollingStack()
    {
        var root = GetRootGrid();

        Assert.Equal("Auto,*,Auto", (string?)root.Attribute("RowDefinitions"));
        Assert.Equal("{DynamicResource SkySpaceXs}", (string?)root.Attribute("RowSpacing"));
        Assert.Equal("{DynamicResource SkyInsetSm}", (string?)root.Attribute("Margin"));
        Assert.DoesNotContain(root.Descendants(Avalonia + "ScrollViewer"), _ => true);
    }

    [Fact]
    public void LoanHistoryList_OwnsTheStarRowAndKeepsASensibleMinimum()
    {
        var list = GetHistoryList();

        Assert.Equal("1", (string?)list.Attribute("Grid.Row"));
        Assert.Equal("{Binding History}", (string?)list.Attribute("ItemsSource"));
        Assert.Equal("160", (string?)list.Attribute("MinHeight"));
        Assert.Null((string?)list.Attribute("MaxHeight"));
    }

    [Fact]
    public void FormRows_StayInTheirNaturalHeightRow()
    {
        var form = GetFormStack();

        Assert.Null((string?)form.Attribute("Grid.Row"));
        Assert.Equal("{DynamicResource SkySpaceXs}", (string?)form.Attribute("Spacing"));
        Assert.Contains(form.Elements(Avalonia + "TextBlock"),
            label => label.ToString().Contains("LoansKeys.LoanHistory", StringComparison.Ordinal));
    }

    [Fact]
    public void BorrowerRow_OffersSelectionAndOptionsNavigation()
    {
        var form = GetFormStack();
        Assert.Single(form.Elements(Avalonia + "ComboBox"));
        Assert.Contains(form.Elements(Avalonia + "Button"),
            button => (string?)button.Attribute("Command") == "{Binding ManageBorrowersCommand}");
        Assert.DoesNotContain(form.Descendants(),
            control => control.Attributes().Any(attribute => attribute.Value is
                "{Binding ContactName}" or "{Binding AddBorrowerCommand}" or "{Binding RenameBorrowerCommand}"));
    }

    [Fact]
    public void SaveCaption_StaysPinnedBelowTheList()
    {
        var caption = GetSaveCaption();

        Assert.Equal("2", (string?)caption.Attribute("Grid.Row"));
        Assert.Contains("LoansKeys.LoanChangesSave", (string?)caption.Attribute("Text"), StringComparison.Ordinal);
        Assert.Equal("Wrap", (string?)caption.Attribute("TextWrapping"));
    }

    private static XElement GetRootGrid()
    {
        var document = XDocument.Parse(ReadRepoFile("Plugins", "SkyCD.Plugin.Feature.Loans", "Views", "LoanEditor.axaml"));
        return Assert.Single(document.Root!.Elements(Avalonia + "Grid"));
    }

    private static XElement GetHistoryList() =>
        Assert.Single(GetRootGrid().Elements(Avalonia + "ListBox"));

    private static XElement GetFormStack() =>
        Assert.Single(GetRootGrid().Elements(Avalonia + "StackPanel"));

    private static XElement GetSaveCaption() =>
        Assert.Single(GetRootGrid().Elements(Avalonia + "TextBlock"));

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

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Repository root with SkyCD.slnx was not found.");
    }
}
