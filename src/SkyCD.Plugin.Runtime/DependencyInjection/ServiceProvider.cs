using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

/// <summary>
/// Runtime container facade with global registration support.
/// </summary>
public static class ServiceProvider
{
    private static readonly IContainer MainContainer;
    private static IContainer _pluginServiceProvider;

    static ServiceProvider()
    {
        var mainContainer = new DryIoc.Container();
        MainContainer = mainContainer;

        AddRegistrator<CommonRuntimeServiceRegistrator>();
        AddRegistrator<CouchbaseServiceRegistrator>();
        AddRegistrator<PluginServiceRegistrator>();

        _pluginServiceProvider = CreatePluginsChildContainer();
    }

    public static void ReregisterPluginsService()
    {
        _pluginServiceProvider.Dispose();
        _pluginServiceProvider = CreatePluginsChildContainer();
    }

    private static IContainer CreatePluginsChildContainer()
    {
        var pluginManager = Resolve<PluginManager>();
        var plugins = pluginManager.Plugins.ToList();
        
        var child = MainContainer.CreateChild();
        PluginServiceRegistrator.RegisterServices(child, plugins);
        
        return child;
    }

    private static void AddRegistrator<TRegistrator>()
        where TRegistrator : IServiceRegistrator, new()
    {
        new TRegistrator().RegisterServices(MainContainer);
    }

    public static T Resolve<T>()
        where T : notnull
    {
        return MainContainer.Resolve<T>();
    }

    public static object Resolve(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return MainContainer.Resolve(serviceType, serviceKey: serviceKey);
    }

    public static T ResolvePlugin<T>()
        where T : notnull
    {
        return _pluginServiceProvider.Resolve<T>();
    }

    public static object ResolvePlugin(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return _pluginServiceProvider.Resolve(serviceType, serviceKey: serviceKey);
    }
}
