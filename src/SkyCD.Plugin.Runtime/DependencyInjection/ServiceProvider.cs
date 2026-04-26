using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Runtime.Factories;
using MsServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

/// <summary>
/// Wrapper around Microsoft DI service provider used by plugin runtime.
/// </summary>
public sealed class ServiceProvider : IDisposable, IKeyedServiceProvider
{
    public static ServiceProvider Instance { get; } = new();

    private readonly IServiceCollection _descriptors = new ServiceCollection();
    private MsServiceProvider _container = new ServiceCollection().BuildServiceProvider();

    public ServiceProvider()
    {
        SeedCommonServices();
        RebuildFromDescriptors();
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _container.GetService(serviceType);
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _container.GetKeyedService(serviceType, serviceKey);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _container.GetRequiredKeyedService(serviceType, serviceKey);
    }

    public void Import(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        if (provider is not MsServiceProvider msProvider)
        {
            throw new ArgumentException(
                "Service provider must be Microsoft.Extensions.DependencyInjection.ServiceProvider.",
                nameof(provider));
        }

        Replace(msProvider);
    }

    public void Import(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Import((IEnumerable<ServiceDescriptor>)services);
    }

    public void Import(IEnumerable<ServiceDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        foreach (var descriptor in descriptors)
        {
            _descriptors.Add(descriptor);
        }

        RebuildFromDescriptors();
    }

    public void Dispose()
    {
        _container.Dispose();
        _descriptors.Clear();
        SeedCommonServices();
        RebuildFromDescriptors();
    }

    private void Replace(MsServiceProvider provider)
    {
        if (ReferenceEquals(_container, provider))
        {
            return;
        }

        _container.Dispose();
        _container = provider;
    }

    private void RebuildFromDescriptors()
    {
        IServiceCollection services = new ServiceCollection();
        foreach (var descriptor in _descriptors)
        {
            services.Add(descriptor);
        }

        Replace(services.BuildServiceProvider());
    }

    private void SeedCommonServices()
    {
        var commonServices = new ServiceCollectionFactory().BuildCommonServiceCollection();
        foreach (var descriptor in commonServices)
        {
            _descriptors.Add(descriptor);
        }
    }
}
