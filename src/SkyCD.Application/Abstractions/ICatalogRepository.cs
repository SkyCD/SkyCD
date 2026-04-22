using SkyCD.Domain.Catalogs;

namespace SkyCD.Application.Abstractions;

public interface ICatalogRepository
{
    Task AddAsync(Catalog catalog, CancellationToken cancellationToken = default);

    Task<Catalog?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Catalog>> ListAsync(CancellationToken cancellationToken = default);

    void Remove(Catalog catalog);
}