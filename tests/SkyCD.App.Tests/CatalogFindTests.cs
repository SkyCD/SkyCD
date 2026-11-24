using System;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CatalogFindTests
{
    private static readonly CatalogDocument[] Documents =
    [
        new() { Id = "disc-a", Name = "Disc A", Type = CatalogDocumentType.Media },
        new() { Id = "photos-a", ParentId = "disc-a", Name = "Photos", Type = CatalogDocumentType.Folder },
        new() { Id = "nested-a", ParentId = "photos-a", Name = "Trips", Type = CatalogDocumentType.Folder },
        new() { Id = "file-a", ParentId = "nested-a", Name = "Sunset.JPG", Type = CatalogDocumentType.File },
        new() { Id = "disc-b", Name = "Disc B", Type = CatalogDocumentType.Media },
        new() { Id = "photos-b", ParentId = "disc-b", Name = "Photos", Type = CatalogDocumentType.Folder },
        new() { Id = "file-b", ParentId = "photos-b", Name = "sunset.jpg", Type = CatalogDocumentType.File }
    ];

    [Fact]
    public void Search_MatchesMixedCaseAcrossNestedCollapsedEntries()
    {
        var result = CatalogFindService.Search(Documents, "SuNsEt");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, match => match.FullPath == "Disc A / Photos / Trips / Sunset.JPG");
    }

    [Fact]
    public void Search_ParentPathMatchIncludesDescendantsAndSeparateMediaDuplicates()
    {
        var result = CatalogFindService.Search(Documents, "photos");

        Assert.Equal(5, result.Count);
        Assert.Equal(2, result.Count(match => match.Document.Name.Equals("sunset.jpg", System.StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(result, match => match.FullPath.StartsWith("Disc A /", System.StringComparison.Ordinal));
        Assert.Contains(result, match => match.FullPath.StartsWith("Disc B /", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ToolbarFindToggle_OpensAndClosesTheQuickFindBar()
    {
        using var catalog = MainWindowViewModel.CreateForInMemoryCatalog(Documents);

        catalog.ToggleQuickFilterCommand.Execute(null);
        Assert.True(catalog.IsQuickFilterOpen);

        catalog.QuickFindQuery = "sunset";
        catalog.ToggleQuickFilterCommand.Execute(null);
        Assert.False(catalog.IsQuickFilterOpen);
        Assert.Equal(string.Empty, catalog.QuickFindQuery);
        Assert.False(catalog.SearchWholeCatalog);
    }

    [Fact]
    public async Task QuickFind_FiltersCurrentChildrenAndRestoresListWhenClearedOrClosed()
    {
        using var catalog = MainWindowViewModel.CreateForInMemoryCatalog(Documents);
        var folder = catalog.TreeNodes.Single(node => node.Key == "disc-a").Children.Single();
        catalog.SelectedTreeNode = folder;
        catalog.FindCatalogCommand.Execute(null);
        Assert.True(catalog.IsQuickFilterOpen);

        catalog.QuickFindQuery = "sunset";
        await WaitForAsync(() => catalog.QuickFindStatus == "No matching catalog entries.");
        Assert.Empty(catalog.VisibleBrowserItems);

        catalog.QuickFindQuery = "tRiP";
        await WaitForAsync(() => catalog.VisibleBrowserItems.Count == 1);
        Assert.Equal("nested-a", Assert.Single(catalog.VisibleBrowserItems).Id);

        catalog.QuickFindQuery = string.Empty;
        Assert.Same(catalog.BrowserItems, catalog.VisibleBrowserItems);

        catalog.QuickFindQuery = "missing";
        catalog.CloseQuickFilterCommand.Execute(null);
        Assert.False(catalog.IsQuickFilterOpen);
        Assert.Same(catalog.BrowserItems, catalog.VisibleBrowserItems);
        Assert.Same(folder, catalog.SelectedTreeNode);
        Assert.False(catalog.IsDirtyDocument);
    }

    [Fact]
    public async Task QuickFind_UsesLatestQueryAndNavigatesToTheChosenDuplicate()
    {
        using var catalog = MainWindowViewModel.CreateForInMemoryCatalog(Documents);
        catalog.FindCatalogCommand.Execute(null);
        catalog.SearchWholeCatalog = true;
        catalog.QuickFindQuery = "photos";
        catalog.QuickFindQuery = "SUNSET";
        await WaitForAsync(() => catalog.QuickFindResults.Count == 2);
        var selected = Assert.Single(catalog.QuickFindResults, result => result.Document.Id == "file-a");
        Assert.Equal("Disc A / Photos / Trips", selected.Context);

        catalog.SelectedQuickFindResult = selected;
        catalog.OpenQuickFindResultCommand.Execute(null);

        Assert.False(catalog.IsQuickFilterOpen);
        Assert.Equal("nested-a", catalog.SelectedTreeNode?.Key);
        Assert.Equal("file-a", catalog.SelectedBrowserItem?.Id);
        Assert.True(catalog.TreeNodes.Single(node => node.Key == "disc-a").Children.Single().IsExpanded);
        Assert.False(catalog.IsDirtyDocument);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    public void Search_EmptyOrMissingQueryReturnsNoMatches(string query)
    {
        Assert.Empty(CatalogFindService.Search(Documents, query));
    }

    [Fact]
    public async System.Threading.Tasks.Task NavigateToResult_ExpandsContextSelectsOriginalAndDoesNotDirtyCatalog()
    {
        using var catalog = MainWindowViewModel.CreateForInMemoryCatalog(Documents);
        using var find = catalog.CreateCatalogFindDialogViewModel();
        find.Query = "sunset.jpg";
        await find.SearchCommand.ExecuteAsync(null);

        var result = Assert.Single(find.Results, match => match.Document.Id == "file-a");
        find.NavigateToResultCommand.Execute(result);

        Assert.Equal("nested-a", catalog.SelectedTreeNode?.Key);
        Assert.Equal("file-a", catalog.SelectedBrowserItem?.Id);
        Assert.True(catalog.TreeNodes.Single(node => node.Key == "disc-a").IsExpanded);
        Assert.False(catalog.IsDirtyDocument);
    }
}
