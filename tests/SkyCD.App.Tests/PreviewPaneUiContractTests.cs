using System;
using System.IO;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Guards the wiring of the offline preview surface in the main window and the find dialog. The behaviour
/// itself is covered by <c>MainWindowPreviewPaneTests</c> and the headless tests in SkyCD.UI.Tests; these
/// assertions only fail if the view stops binding the pane or the matched-field column.
/// </summary>
public sealed class PreviewPaneUiContractTests
{
    [Fact]
    public void MainWindow_HostsThePreviewPaneAndFeedsItTheResolvedState()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");

        Assert.Contains("xmlns:pv=\"clr-namespace:SkyCD.UI.Controls.Preview;assembly=SkyCD.UI\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<pv:MediaPreviewPane x:Name=\"MainPreviewPane\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsPreviewPaneVisible}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("State=\"{Binding Preview.State}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewImage=\"{Binding Preview.PreviewImage}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewText=\"{Binding Preview.PreviewText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StateMessage=\"{Binding Preview.StateMessage}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Fields=\"{Binding Preview.Fields}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewAutomationName=\"{Binding Preview.PreviewAutomationName}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_BrowserList_UsesTheCachedThumbnailProvider()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");

        Assert.Contains("ThumbnailProvider=\"{Binding BrowserThumbnailProvider}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FindDialog_ShowsWhichCapturedFieldMatched()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "CatalogFindWindow.axaml");

        Assert.Contains("Text=\"{Binding MatchedField}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasMatchedField}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PreviewTranslations_ShipForEverySupportedCulture()
    {
        foreach (var culture in new[] { "en", "lt" })
        {
            var yaml = ReadRepoFile("src", "SkyCD.UI", "Translations", $"{culture}-Preview.yaml");

            Assert.Contains("preview.PreviewPane:", yaml, StringComparison.Ordinal);
            Assert.Contains("preview.StateStale:", yaml, StringComparison.Ordinal);
            Assert.Contains("preview.PreviewArtifactMissing:", yaml, StringComparison.Ordinal);
            Assert.Contains("preview.FieldsAutomation:", yaml, StringComparison.Ordinal);
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
