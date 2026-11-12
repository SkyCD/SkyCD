using System.Linq;
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
