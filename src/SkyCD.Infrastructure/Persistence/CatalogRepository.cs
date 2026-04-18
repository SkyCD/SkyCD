using Microsoft.EntityFrameworkCore;
using SkyCD.Application.Abstractions;
using SkyCD.Domain.Catalogs;

namespace SkyCD.Infrastructure.Persistence;

public sealed class CatalogRepository(SkyCdDbContext dbContext) : ICatalogRepository
{
    public async Task AddAsync(Catalog catalog, CancellationToken cancellationToken = default)
    {
        await dbContext.Catalogs.AddAsync(catalog, cancellationToken);
    }

    public async Task<Catalog?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Catalogs
            .Include(catalog => catalog.Nodes)
            .Include(catalog => catalog.Tags)
            .FirstOrDefaultAsync(catalog => catalog.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Catalog>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.Catalogs
            .AsNoTracking()
            .OrderBy(catalog => catalog.Name)
            .ToListAsync(cancellationToken);
    }

    public void Remove(Catalog catalog)
    {
        dbContext.Catalogs.Remove(catalog);
    }
}
