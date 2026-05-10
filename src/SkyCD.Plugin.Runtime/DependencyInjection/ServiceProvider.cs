using System;
using System.Collections.Generic;
using DryIoc;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

/// <summary>
/// Runtime container with global registration replay support.
/// </summary>
public sealed class Container : DryIoc.Container
{
    private static Container? _instance;

    public static Container Instance
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
        _instance = new Container(static _ => { })
            .AddRegistrator<CommonRuntimeServiceRegistrator>()
            .AddRegistrator<CouchbaseServiceRegistrator>()
            .AddRegistrator<PluginServiceRegistrator>();
    }

    private readonly List<Action<IContainer>> registrations = [];

    public Container(Action<IContainer> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        Register(register);
    }

    public Container AddRegistrator<TRegistrator>()
        where TRegistrator : IServiceRegistrator, new()
    {
        var registrator = new TRegistrator();
        registrator.RegisterServices(this);
        registrations.Add(static container => new TRegistrator().RegisterServices(container));
        return this;
    }

    public void Register(Action<IContainer> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        register(this);
        registrations.Add(register);
    }

    public IContainer CreateSubcontainer(Action<IContainer> register)
    {
        ArgumentNullException.ThrowIfNull(register);
        var child = With(Rules, ScopeContext, RegistrySharing.CloneAndDropCache, SingletonScope);
        register(child);
        return child;
    }
}
