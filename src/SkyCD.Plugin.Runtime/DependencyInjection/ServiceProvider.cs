using System;
using DryIoc;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

/// <summary>
/// Runtime container with global registration replay support.
/// </summary>
public sealed class ServiceProvider : DryIoc.Container
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
        _instance = new ServiceProvider(static _ => { })
            .AddRegistrator<CommonRuntimeServiceRegistrator>()
            .AddRegistrator<CouchbaseServiceRegistrator>()
            .AddRegistrator<PluginServiceRegistrator>();
    }

    public ServiceProvider(Action<IContainer> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        Register(register);
    }

    public ServiceProvider AddRegistrator<TRegistrator>()
        where TRegistrator : IServiceRegistrator, new()
    {
        var registrator = new TRegistrator();
        registrator.RegisterServices(this);
        return this;
    }

    public void Register(Action<IContainer> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        register(this);
    }

    public IContainer CreateSubcontainer(Action<IContainer> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        var child = With(Rules, ScopeContext, RegistrySharing.CloneAndDropCache, SingletonScope);
        register(child);
        return child;
    }
}
