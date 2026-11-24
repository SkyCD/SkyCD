using SkyCD.Documents.Enum;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class DropDestinationTests
{
    [Theory]
    [InlineData(CatalogDocumentType.Media)]
    [InlineData(CatalogDocumentType.Folder)]
    [InlineData(CatalogDocumentType.NetworkFolder)]
    [InlineData(CatalogDocumentType.NetworkResource)]
    public void ContainerTypes_ResolveToTheItemItself(CatalogDocumentType type)
    {
        var destination = DropDestination.FromCatalogItem("item-1", "Trip data", type);

        Assert.NotNull(destination);
        Assert.Equal("item-1", destination.Value.ParentId);
        Assert.Equal("Trip data", destination.Value.DisplayName);
    }

    [Fact]
    public void FileItems_AreNotDropParents()
    {
        Assert.Null(DropDestination.FromCatalogItem("file-1", "clip.trf", CatalogDocumentType.File));
    }

    [Fact]
    public void LibraryRoot_KeepsTheCatalogSentinelId()
    {
        Assert.Equal("library", DropDestination.LibraryRootId);
        Assert.Equal("library", DropDestination.LibraryRoot.ParentId);
        Assert.Null(DropDestination.LibraryRoot.DisplayName);
    }

    [Fact]
    public void TreeNodes_UseTheirCatalogKeyAsParent()
    {
        var destination = DropDestination.FromTreeNode("node-7", "Concerts");

        Assert.Equal("node-7", destination.ParentId);
        Assert.Equal("Concerts", destination.DisplayName);
    }
}
