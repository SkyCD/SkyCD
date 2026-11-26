using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Presentation.ViewModels;
using SkyCD.SDK.Capabilities.Menu;
using Xunit;

namespace SkyCD.App.Tests;

public class FilePluginMenuTests
{
    private const string NfoKey = "file_plugin_demo.nfo";
    private const string DescriptionKey = "file_plugin_demo.description";

    [Fact]
    public void FileLocationContributions_AreGroupedBetweenSeparatorsInOrder()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        vm.RefreshPluginMenuServices(new MenuExtensionManager([new FileMenuPlugin()]));

        var items = vm.FileMenuItems.ToList();
        var first = items.FindIndex(item => item.Key == NfoKey);
        var second = items.FindIndex(item => item.Key == DescriptionKey);

        Assert.True(first > 1);
        Assert.Equal(first + 1, second);
        Assert.Equal("Generate .nfo...", items[first].Header);
        Assert.Equal("Generate description...", items[second].Header);
        Assert.Equal("-", items[first - 1].Header);
        Assert.NotEqual("-", items[first - 2].Header);
        Assert.Equal("-", items[second + 1].Header);
    }

    [Fact]
    public void FileLocationContributions_WithoutLocalFileRequirement_AreNotGated()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(Catalog());
        vm.RefreshPluginMenuServices(new MenuExtensionManager([new FileMenuPlugin(requiresLocalFile: false)]));

        vm.SelectedTreeNode = vm.TreeNodes[0].Children.Single(node => node.Key == "files");

        Assert.True(MenuItem(vm, NfoKey).IsEnabled);
    }

    [Theory]
    [InlineData("notes.txt", "files", true)]
    [InlineData("remote.bin", "nas", false)]
    [InlineData("unknown.txt", "files", false)]
    [InlineData("movie.mkv", "files", false)]
    [InlineData("subfolder", "files", false)]
    [InlineData("link", "files", false)]
    public void RequiresLocalFile_IsEnabledOnlyForLocalFiles(string entryName, string folderKey, bool expected)
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(Catalog());
        vm.RefreshPluginMenuServices(new MenuExtensionManager([new FileMenuPlugin()]));

        vm.SelectedTreeNode = vm.TreeNodes[0].Children.Single(node => node.Key == folderKey);
        vm.SelectedBrowserItem = vm.BrowserItems.Single(item => item.Name == entryName);

        Assert.Equal(expected, MenuItem(vm, NfoKey).IsEnabled);
        Assert.Equal(expected, MenuItem(vm, DescriptionKey).IsEnabled);
    }

    private static MainMenuItemViewModel MenuItem(MainWindowViewModel vm, string key) =>
        vm.FileMenuItems.Single(item => item.Key == key);

    private static IReadOnlyList<CatalogDocument> Catalog()
    {
        static PropertiesCollection Path(string value) =>
            new(new Dictionary<string, object?> { ["FullName"] = value });

        return
        [
            new CatalogDocument
            {
                Id = "library",
                Name = "Library",
                ParentId = null,
                Type = CatalogDocumentType.Folder,
                ChildrenCount = 2
            },
            new CatalogDocument
            {
                Id = "files",
                Name = "Files",
                ParentId = "library",
                Type = CatalogDocumentType.Folder,
                ChildrenCount = 5
            },
            new CatalogDocument
            {
                Id = "notes",
                Name = "notes.txt",
                ParentId = "files",
                Type = CatalogDocumentType.File,
                Size = 120,
                Properties = Path(@"C:\media\notes.txt")
            },
            new CatalogDocument
            {
                Id = "unknown",
                Name = "unknown.txt",
                ParentId = "files",
                Type = CatalogDocumentType.File,
                Size = 300
            },
            new CatalogDocument
            {
                Id = "movie",
                Name = "movie.mkv",
                ParentId = "files",
                Type = CatalogDocumentType.Media,
                Size = 900,
                Properties = Path(@"C:\media\movie.mkv")
            },
            new CatalogDocument
            {
                Id = "subfolder",
                Name = "subfolder",
                ParentId = "files",
                Type = CatalogDocumentType.Folder,
                ChildrenCount = 0,
                Properties = Path(@"C:\media\subfolder")
            },
            new CatalogDocument
            {
                Id = "link",
                Name = "link",
                ParentId = "files",
                Type = CatalogDocumentType.NetworkResource,
                Properties = Path(@"\\server\share\link")
            },
            new CatalogDocument
            {
                Id = "nas",
                Name = "NAS",
                ParentId = "library",
                Type = CatalogDocumentType.NetworkFolder,
                ChildrenCount = 1,
                Properties = Path(@"\\server\share")
            },
            new CatalogDocument
            {
                Id = "remote",
                Name = "remote.bin",
                ParentId = "nas",
                Type = CatalogDocumentType.File,
                Size = 42,
                Properties = Path(@"\\server\share\remote.bin")
            }
        ];
    }

    private sealed class FileMenuPlugin(bool requiresLocalFile = true) : IMenuPluginCapability
    {
        public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
        [
            new MenuContribution("demo.nfo", "Generate .nfo...", "File", 10, requiresLocalFile),
            new MenuContribution("demo.description", "Generate description...", "File", 20, requiresLocalFile)
        ];

        public Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
