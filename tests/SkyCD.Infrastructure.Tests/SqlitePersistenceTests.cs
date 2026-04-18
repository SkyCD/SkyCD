using Microsoft.EntityFrameworkCore;
using SkyCD.Domain.Catalogs;
using SkyCD.Infrastructure.Persistence;

namespace SkyCD.Infrastructure.Tests;

public sealed class SqlitePersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"skycd-{Guid.NewGuid():N}.db");

    [Fact]
    public void Migrate_CreatesSqliteDatabaseSchema()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        Assert.True(File.Exists(_dbPath));
        Assert.Contains("Catalogs", context.Database.GenerateCreateScript(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_CanCreateReadUpdateDeleteCatalog()
    {
        await using var context = CreateContext();
        await context.Database.MigrateAsync();

        var repository = new CatalogRepository(context);
        var unitOfWork = new EfUnitOfWork(context);

        var catalog = new Catalog
        {
            Name = "Integration Catalog",
            SchemaVersion = 1
        };

        catalog.Nodes.Add(new CatalogNode
        {
            Name = "Root",
            Kind = CatalogNodeKind.Folder
        });

        await repository.AddAsync(catalog);
        await unitOfWork.SaveChangesAsync();

        var loaded = await repository.GetAsync(catalog.Id);
        Assert.NotNull(loaded);
        Assert.Equal("Integration Catalog", loaded.Name);
        Assert.Single(loaded.Nodes);

        loaded.Name = "Updated Catalog";
        await unitOfWork.SaveChangesAsync();

        var refreshed = await repository.GetAsync(catalog.Id);
        Assert.NotNull(refreshed);
        Assert.Equal("Updated Catalog", refreshed.Name);

        repository.Remove(refreshed);
        await unitOfWork.SaveChangesAsync();

        var deleted = await repository.GetAsync(catalog.Id);
        Assert.Null(deleted);
    }

    private SkyCdDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SkyCdDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        return new SkyCdDbContext(options);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch (IOException)
            {
                // SQLite may release file handles slightly after DbContext disposal on some platforms.
            }
        }
    }
}
