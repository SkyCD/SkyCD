using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;

namespace SkyCD.Application.Abstractions;

public interface ICatalogRepository
{
    Task AddAsync(CatalogDocument catalog, CancellationToken cancellationToken = default);

    Task<CatalogDocument?> GetAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CatalogDocument>> ListAsync(CancellationToken cancellationToken = default);

    void Remove(CatalogDocument catalog);
}
