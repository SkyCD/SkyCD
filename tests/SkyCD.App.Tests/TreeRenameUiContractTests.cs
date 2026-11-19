using System;
using System.IO;
using Xunit;

namespace SkyCD.App.Tests;

public class TreeRenameUiContractTests
{
    [Fact]
    public void MainWindow_Tree_ProvidesTheInlineRenameEditor()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");

        Assert.Contains("Text=\"{Binding RenameText, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsRenaming}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding !IsRenaming}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KeyDown=\"OnRenameEditorKeyDown\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LostFocus=\"OnRenameEditorLostFocus\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Classes.invalid=\"{Binding HasRenameError}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LocalizationCatalogs_ShipRenameLabelsForEverySupportedCulture()
    {
        foreach (var culture in new[] { "en", "lt" })
        {
            var ui = ReadRepoFile("src", "SkyCD.UI", "Translations", $"{culture}-UI.yaml");
            var text = ReadRepoFile("src", "SkyCD.UI", "Translations", $"{culture}-Text.yaml");

            Assert.Contains("ui.Rename:", ui, StringComparison.Ordinal);
            Assert.Contains("text.Rename:", text, StringComparison.Ordinal);
            Assert.Contains("text.Renamed:", text, StringComparison.Ordinal);
            Assert.Contains("text.CatalogNameRequired:", text, StringComparison.Ordinal);
            Assert.Contains("text.RenameStaleEntry:", text, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine([root, .. parts]));
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}