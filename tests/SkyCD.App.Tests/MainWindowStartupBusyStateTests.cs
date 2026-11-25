using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1109: the shell must be painted before the plugin runtime and the last catalog are restored,
/// and the browser area must show a busy state for as long as the restore runs.
/// </summary>
public sealed class MainWindowStartupBusyStateTests
{
    [Fact]
    public void BrowserArea_ShowsBusyOverlayWhileTheCatalogLoads()
    {
        var document = XDocument.Parse(ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml"));
        XNamespace avalonia = "https://github.com/avaloniaui";

        var browser = document.Descendants().Single(element => element.Name.LocalName == "BrowserItemsView");
        var browserArea = browser.Ancestors(avalonia + "Grid").First();
        var overlay = browserArea.Elements(avalonia + "Border").SingleOrDefault(element =>
            (string?)element.Attribute("IsVisible") == "{Binding IsCatalogLoading}");

        Assert.NotNull(overlay);
        Assert.Equal("{DynamicResource ThemeBackgroundBrush}", (string?)overlay!.Attribute("Background"));
        Assert.Contains(overlay.Descendants(avalonia + "ProgressBar"),
            progress => (string?)progress.Attribute("Value") == "{Binding ProgressValue}");
        Assert.Contains(overlay.Descendants(avalonia + "TextBlock"),
            text => (string?)text.Attribute("Text") == "{Binding StatusText}");
    }

    [Fact]
    public void StartupRestore_PaintsTheShellBeforeLoadingPluginsAndTheLastCatalog()
    {
        var opened = ExtractMethod(ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml.cs"),
            "private async void OnOpened(");

        var beginLoad = opened.IndexOf("vm.BeginCatalogLoad();", StringComparison.Ordinal);
        var yieldFirstFrame = opened.IndexOf("DispatcherPriority.Background", StringComparison.Ordinal);
        var loadPlugins = opened.IndexOf("EnsureFileFormatProvidersLoaded();", StringComparison.Ordinal);
        var loadCatalog = opened.IndexOf("await TryLoadCatalogIntoViewModelAsync(", StringComparison.Ordinal);

        Assert.True(beginLoad >= 0, "OnOpened must publish the busy state before the restore starts.");
        Assert.True(yieldFirstFrame > beginLoad,
            "OnOpened must yield to the dispatcher so the shell paints before the restore starts.");
        Assert.True(loadPlugins > yieldFirstFrame,
            "Plugin discovery must run only after the shell has painted its first frame.");
        Assert.True(loadCatalog > loadPlugins,
            "The last catalog must be restored after the file-format providers are loaded.");
        Assert.Contains("vm.EndCatalogLoad();", opened, StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find '{signature}'.");
        var end = source.IndexOf("\n    private ", start, StringComparison.Ordinal);
        return end < 0 ? source[start..] : source[start..end];
    }

    private static string ReadRepoFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var fullPath = Path.Combine([current.FullName, .. parts]);
            if (File.Exists(fullPath))
            {
                return File.ReadAllText(fullPath);
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
