using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPluginService<TServiceType, TServiceClass>(this IServiceCollection services)
        where TServiceType : class
        where TServiceClass : class, TServiceType
    {
        services.AddSingleton<TServiceType, TServiceClass>();
        services.AddKeyedSingleton<TServiceType, TServiceClass>(typeof(TServiceType));

        return services;
    }

    public static IServiceCollection AddPluginService(this IServiceCollection services, Type serviceType, object serviceInstance)
    {
        services.AddSingleton(serviceType, serviceInstance);
        services.AddKeyedSingleton(serviceType, serviceType, serviceInstance);

        return services;
    }

    public static IServiceCollection AddRegistrator<TRegistrator>(this IServiceCollection services)
        where TRegistrator : IServiceRegistrator
    {
        TRegistrator.RegisterServices(services);
        return services;
    }

    public static IServiceCollection AddPluginRegistrator(
        this IServiceCollection services,
        DiscoveredPlugin plugin)
    {
        PluginServiceRegistrator.RegisterServices(services, plugin);
        return services;
    }

    public static IServiceCollection AddPluginRegistrator(
        this IServiceCollection services,
        IEnumerable<DiscoveredPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(plugins);

        foreach (var plugin in plugins)
        {
            PluginServiceRegistrator.RegisterServices(services, plugin);
        }

        return services;
    }
}
