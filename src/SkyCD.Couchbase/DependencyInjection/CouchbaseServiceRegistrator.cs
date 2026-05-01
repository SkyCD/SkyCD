using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection;

namespace SkyCD.Couchbase.DependencyInjection;

public sealed class CouchbaseServiceRegistrator : IServiceRegistrator
{
    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<DatabaseManager>();
        services.AddSingleton<RepositoryManager>();
    }
}
