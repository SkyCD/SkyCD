using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using MsServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

/// <summary>
/// Wrapper around Microsoft DI service provider used by plugin runtime.
/// </summary>
public sealed class ServiceProvider : IDisposable, IKeyedServiceProvider
{
    private static ServiceProvider? _instance;

    public static ServiceProvider Instance
    {
        get
        {
            if (_instance is null)
            {
                RebuildGlobal();
            }

            return _instance!;
        }
    }

    public static void RebuildGlobal()
    {
        var services = new ServiceCollection();
        services
            .AddRegistrator<CommonRuntimeServiceRegistrator>()
            .AddRegistrator<CouchbaseServiceRegistrator>()
            .AddRegistrator<PluginServiceRegistrator>();

        _instance = new ServiceProvider(services);
    }

    private readonly IServiceCollection descriptors = new ServiceCollection();
    private MsServiceProvider container;

    public ServiceProvider(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var descriptor in services)
        {
            descriptors.Add(descriptor);
        }

        container = new ServiceCollection().BuildServiceProvider();
        RebuildFromDescriptors();
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return container.GetService(serviceType);
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return container.GetKeyedService(serviceType, serviceKey);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return container.GetRequiredKeyedService(serviceType, serviceKey);
    }

    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Register((IEnumerable<ServiceDescriptor>)services);
    }

    public void Register(IEnumerable<ServiceDescriptor> serviceDescriptors)
    {
        ArgumentNullException.ThrowIfNull(serviceDescriptors);

        foreach (var descriptor in serviceDescriptors)
        {
            descriptors.Add(descriptor);
        }

        RebuildFromDescriptors();
    }

    public void Dispose()
    {
        container.Dispose();
    }

    private void RebuildFromDescriptors()
    {
        IServiceCollection services = new ServiceCollection();
        foreach (var descriptor in descriptors)
        {
            services.Add(descriptor);
        }

        var previous = container;
        container = services.BuildServiceProvider();
        previous.Dispose();
    }

}
