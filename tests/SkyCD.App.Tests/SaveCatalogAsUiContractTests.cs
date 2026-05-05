using System;
using System.IO;
using Xunit;

namespace SkyCD.App.Tests;

public class SaveCatalogAsUiContractTests
{
    [Fact]
    public void MainWindow_DataContextSubscription_WiresSaveCatalogAsRequestedExactlyOnce()
    {
        var codeBehind = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml.cs");

        Assert.Equal(
            1,
            CountOccurrences(
                codeBehind,
                "subscribedViewModel.SaveCatalogAsRequested += OnSaveCatalogAsRequested;"));
        Assert.Equal(
            1,
            CountOccurrences(
                codeBehind,
                "subscribedViewModel.SaveCatalogAsRequested -= OnSaveCatalogAsRequested;"));
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
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

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
