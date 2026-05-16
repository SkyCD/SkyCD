using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DryIoc;
using SkyCD.Couchbase.DependencyInjection;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Core.DependencyInjection;

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
        var oldPluginServiceProvider = _pluginServiceProvider;
        _pluginServiceProvider = CreatePluginsChildContainer();
        oldPluginServiceProvider.Dispose();
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
        where TRegistrator : new()
    {
        var registrator = new TRegistrator();
        var registerMethod = typeof(TRegistrator).GetMethod(
            "RegisterServices",
            BindingFlags.Instance | BindingFlags.Public,
            [typeof(IRegistrator)]);
        if (registerMethod is null)
        {
            throw new InvalidOperationException(
                $"Registrator {typeof(TRegistrator).FullName} must expose RegisterServices(IRegistrator).");
        }

        registerMethod.Invoke(registrator, [MainContainer]);
    }

    public static T Resolve<T>()
        where T : notnull
    {
        if (_pluginServiceProvider is not null)
        {
            var pluginService = _pluginServiceProvider.Resolve<T>(IfUnresolved.ReturnDefault);
            if (pluginService is not null)
            {
                return pluginService;
            }
        }

        return MainContainer.Resolve<T>();
    }

    public static IContainer RegisterChildContainer(Action<IRegistrator> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var child = MainContainer.CreateChild();
        configure(child);
        return child;
    }

    public static object Resolve(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (_pluginServiceProvider is not null)
        {
            var pluginService = _pluginServiceProvider.Resolve(serviceType, serviceKey: serviceKey, ifUnresolved: IfUnresolved.ReturnDefault);
            if (pluginService is not null)
            {
                return pluginService;
            }
        }

        return MainContainer.Resolve(serviceType, serviceKey: serviceKey);
    }
}

