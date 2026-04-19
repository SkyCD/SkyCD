using System.Text.RegularExpressions;

namespace SkyCD.App.Tests;

public class Issue161UiContractTests
{
    [Fact]
    public void MainWindow_UsesSharedFileToolbar()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "MainWindow.axaml");

        Assert.Contains("<cc:FileToolbar", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileToolbar_HasExactlyNewOpenSaveButtonsInOrder()
    {
        var xaml = ReadRepoFile("src", "SkyCD.UI", "Controls", "FileToolbar.axaml");

        var buttonCount = Regex.Matches(xaml, "<Button ").Count;
        Assert.Equal(3, buttonCount);

        var newIndex = xaml.IndexOf("Text=\"_New\"", StringComparison.Ordinal);
        var openIndex = xaml.IndexOf("Text=\"_Open\"", StringComparison.Ordinal);
        var saveIndex = xaml.IndexOf("Text=\"_Save\"", StringComparison.Ordinal);

        Assert.True(newIndex >= 0);
        Assert.True(openIndex > newIndex);
        Assert.True(saveIndex > openIndex);
    }

    [Fact]
    public void PropertiesWindow_UsesSharedTabsAndPropertiesListControls()
    {
        var xaml = ReadRepoFile("src", "SkyCD.App", "Views", "PropertiesWindow.axaml");

        Assert.Contains("<cc:PropertiesTabControl", xaml, StringComparison.Ordinal);
        Assert.Contains("<cc:PropertiesList", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedPropertiesList_UsesDetailsHeadersWithoutClickableHeaderControls()
    {
        var xaml = ReadRepoFile("src", "SkyCD.UI", "Controls", "PropertiesList.axaml");

        Assert.Contains("PropertyHeader", xaml, StringComparison.Ordinal);
        Assert.Contains("ValueHeader", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Button", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCustomControls_AvoidHardcodedColorLiterals()
    {
        var files = new[]
        {
            ReadRepoFile("src", "SkyCD.UI", "Controls", "FileToolbar.axaml"),
            ReadRepoFile("src", "SkyCD.UI", "Controls", "PropertiesTabControl.axaml"),
            ReadRepoFile("src", "SkyCD.UI", "Controls", "PropertiesList.axaml")
        };

        foreach (var xaml in files)
        {
            Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{3,8}"), xaml);
        }
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
