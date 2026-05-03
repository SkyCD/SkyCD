using Microsoft.Extensions.DependencyInjection;

namespace SkyCD.Couchbase.DependencyInjection;

public interface IServiceRegistrator
{
    static abstract void RegisterServices(IServiceCollection services);
}
