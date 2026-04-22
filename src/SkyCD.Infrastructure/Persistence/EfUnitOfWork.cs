using SkyCD.Application.Abstractions;

namespace SkyCD.Infrastructure.Persistence;

public sealed class EfUnitOfWork(SkyCdDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}