using Microsoft.Extensions.DependencyInjection;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRegistrator<TRegistrator>(this IServiceCollection services)
        where TRegistrator : IServiceRegistrator
    {
        TRegistrator.RegisterServices(services);
        return services;
    }
}
