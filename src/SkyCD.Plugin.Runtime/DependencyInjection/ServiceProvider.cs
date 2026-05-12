using System;
using DryIoc;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

/// <summary>
/// Runtime container facade with global registration support.
/// </summary>
public static class ServiceProvider
{
    private static IContainer _instance;

    static ServiceProvider()
    {
        _instance = BuildGlobalContainer();
    }

    public static void RebuildGlobal()
    {
        _instance = BuildGlobalContainer();
    }

    public static void AddRegistrator<TRegistrator>()
        where TRegistrator : IServiceRegistrator, new()
    {
        new TRegistrator().RegisterServices(_instance);
    }

    public static T Resolve<T>()
        where T : notnull
    {
        return _instance.Resolve<T>();
    }

    public static object Resolve(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _instance.Resolve(serviceType, serviceKey: serviceKey);
    }

    private static IContainer BuildGlobalContainer()
    {
        var container = new DryIoc.Container();
        new CommonRuntimeServiceRegistrator().RegisterServices(container);
        new CouchbaseServiceRegistrator().RegisterServices(container);
        new PluginServiceRegistrator().RegisterServices(container);
        return container;
    }
}
