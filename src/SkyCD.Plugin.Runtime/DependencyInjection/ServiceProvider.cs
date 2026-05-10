using System;
using System.Collections.Generic;
using DryIoc;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

/// <summary>
/// Wrapper around DryIoc container used by plugin runtime.
/// </summary>
public sealed class ServiceProvider : IDisposable
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
        _instance = new ServiceProvider(registrator =>
        {
            registrator
                .AddRegistrator<CommonRuntimeServiceRegistrator>()
                .AddRegistrator<CouchbaseServiceRegistrator>()
                .AddRegistrator<PluginServiceRegistrator>();
        });
    }

    private readonly List<Action<IContainer>> registrations = [];
    private IContainer container;

    public ServiceProvider(Action<IContainer> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        container = new Container();
        Register(register);
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return container.Resolve(serviceType, ifUnresolved: IfUnresolved.ReturnDefault);
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return container.Resolve(serviceType, serviceKey: serviceKey, ifUnresolved: IfUnresolved.ReturnDefault);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return container.Resolve(serviceType, serviceKey: serviceKey, ifUnresolved: IfUnresolved.Throw);
    }

    public T GetRequiredService<T>() where T : notnull
    {
        return container.Resolve<T>();
    }

    public object GetRequiredService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return container.Resolve(serviceType);
    }

    public void Register(Action<IContainer> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        registrations.Add(register);
        RebuildContainer();
    }

    public ServiceProvider CreateSubcontainer(Action<IContainer> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        var snapshot = registrations.ToArray();
        return new ServiceProvider(container =>
        {
            foreach (var registration in snapshot)
            {
                registration(container);
            }

            register(container);
        });
    }

    public void Dispose()
    {
        container.Dispose();
    }

    private void RebuildContainer()
    {
        var next = new Container();
        foreach (var registration in registrations)
        {
            registration(next);
        }
        var previous = container;
        container = next;
        previous.Dispose();
    }
}
