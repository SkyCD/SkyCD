using SkyCD.Domain.Catalogs;

namespace SkyCD.Domain.Tests;

public class CatalogValidatorTests
{
    [Fact]
    public void Validate_ReturnsNoErrors_ForValidCatalog()
    {
        var catalog = new Catalog
        {
            Name = "Main catalog",
            SchemaVersion = 1
        };

        catalog.Nodes.Add(new CatalogNode
        {
            Id = 1,
            CatalogId = catalog.Id,
            Kind = CatalogNodeKind.Folder,
            Name = "Root",
            ParentId = null
        });

        catalog.Nodes.Add(new CatalogNode
        {
            Id = 2,
            CatalogId = catalog.Id,
            Kind = CatalogNodeKind.File,
            Name = "movie.mkv",
            ParentId = 1,
            SizeBytes = 1024
        });

        var errors = CatalogValidator.Validate(catalog);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ReturnsErrors_ForInvalidCatalog()
    {
        var catalog = new Catalog
        {
            Name = "",
            SchemaVersion = 0
        };

        catalog.Nodes.Add(new CatalogNode
        {
            Id = 1,
            CatalogId = catalog.Id,
            Kind = CatalogNodeKind.Folder,
            Name = "",
            SizeBytes = 12
        });

        var errors = CatalogValidator.Validate(catalog);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, error => error.Contains("Catalog.Name"));
        Assert.Contains(errors, error => error.Contains("Catalog.SchemaVersion"));
        Assert.Contains(errors, error => error.Contains("CatalogNode[1].Name"));
        Assert.Contains(errors, error => error.Contains("CatalogNode[1].SizeBytes"));
    }
}