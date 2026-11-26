using System;
using System.IO;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1109: the shell must be painted before the plugin runtime and the last catalog are restored,
/// and nothing may rebuild the outgoing view while the restore runs, so the window keeps drawing.
/// </summary>
public sealed class MainWindowStartupRestoreTests
{
    [Fact]
    public void StartupRestore_PaintsTheShellBeforeLoadingPluginsAndTheLastCatalog()
    {
        var opened = ExtractMethod(ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.Lifecycle.cs"),
            "private async Task OpenedAsync(");

        var beginLoad = opened.IndexOf("vm.BeginCatalogLoad();", StringComparison.Ordinal);
        var yieldFirstFrame = opened.IndexOf("DispatcherPriority.Background", StringComparison.Ordinal);
        var loadPlugins = opened.IndexOf("EnsureFileFormatProvidersLoaded();", StringComparison.Ordinal);
        var loadCatalog = opened.IndexOf("await catalogFiles.LoadAsync(", StringComparison.Ordinal);

        Assert.True(beginLoad >= 0, "OnOpened must publish the busy state before the restore starts.");
        Assert.True(yieldFirstFrame > beginLoad,
            "OnOpened must yield to the dispatcher so the shell paints before the restore starts.");
        Assert.True(loadPlugins > yieldFirstFrame,
            "Plugin discovery must run only after the shell has painted its first frame.");
        Assert.True(loadCatalog > loadPlugins,
            "The last catalog must be restored after the file-format providers are loaded.");
        Assert.Contains("vm.EndCatalogLoad();", opened, StringComparison.Ordinal);
    }

    [Fact]
    public void FeatureDataChanges_DoNotRebuildTheViewWhileTheCatalogLoads()
    {
        var handler = ExtractMethod(
            ReadRepoFile("src", "SkyCD.Presentation", "ViewModels", "MainWindowViewModel.cs"),
            "private void OnCatalogFeatureDataChanged(");

        var loadingGuard = handler.IndexOf("if (IsCatalogLoading)", StringComparison.Ordinal);
        var refreshTree = handler.IndexOf("RefreshTreeNodes();", StringComparison.Ordinal);

        Assert.True(loadingGuard >= 0,
            "Feature data changes raised during a catalog load must not rebuild the outgoing view.");
        Assert.True(refreshTree > loadingGuard,
            "The loading guard must run before the view refresh.");
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
