using System;
using System.Collections.Generic;
using System.Reflection;
using DryIoc;
using SkyCD.Plugin.Runtime.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Exceptions;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

public static class RegistratorExtensions
{
    public static IRegistrator AddPluginService<TServiceType, TServiceClass>(this IRegistrator registrator)
        where TServiceType : class
        where TServiceClass : class, TServiceType
    {
        registrator.Register<TServiceType, TServiceClass>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.AppendNewImplementation);
        registrator.Register<TServiceType, TServiceClass>(Reuse.Singleton, serviceKey: typeof(TServiceType), ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        return registrator;
    }

    public static IRegistrator AddPluginService(this IRegistrator registrator, Type serviceType, object serviceInstance)
    {
        registrator.RegisterInstance(serviceType, serviceInstance, ifAlreadyRegistered: IfAlreadyRegistered.AppendNewImplementation);
        registrator.RegisterInstance(serviceType, serviceInstance, serviceKey: serviceType, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        return registrator;
    }

    public static IRegistrator AddRegistrator<TRegistrator>(this IRegistrator registrator)
    {
        ArgumentNullException.ThrowIfNull(registrator);

        var registerMethod = typeof(TRegistrator).GetMethod(
            "RegisterServices",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(IRegistrator)]);

        if (registerMethod is null)
        {
            throw new RegistratorMethodMissingException(typeof(TRegistrator));
        }

        registerMethod.Invoke(null, [registrator]);
        return registrator;
    }

    public static IRegistrator AddPluginRegistrator(
        this IRegistrator registrator,
        DiscoveredPlugin plugin)
    {
        PluginServiceRegistrator.RegisterServices(registrator, plugin);
        return registrator;
    }

    public static IRegistrator AddPluginRegistrator(
        this IRegistrator registrator,
        IEnumerable<DiscoveredPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(registrator);
        ArgumentNullException.ThrowIfNull(plugins);

        foreach (var plugin in plugins)
        {
            PluginServiceRegistrator.RegisterServices(registrator, plugin);
        }

        return registrator;
    }
}
