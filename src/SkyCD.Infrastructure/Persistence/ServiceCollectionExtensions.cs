using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkyCD.Application.Abstractions;

namespace SkyCD.Infrastructure.Persistence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSkyCdSqlitePersistence(this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<SkyCdDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<ICatalogRepository, CatalogRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        return services;
    }
}