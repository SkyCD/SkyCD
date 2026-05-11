using System;
using System.Linq;
using System.Collections.Generic;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Runtime.DependencyInjection.Registrators;

public sealed class PluginServiceRegistrator : IServiceRegistrator
{
    public void RegisterServices(IRegistrator registrator)
    {
        registrator.Register<AssembliesListFactory>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<DiscoveredPluginFactory>(Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterDelegate<IRepository<PluginDocument>>(static resolver =>
        {
            var repositoryManager = resolver.Resolve<RepositoryManager>();
            return (IRepository<PluginDocument>)repositoryManager.For<PluginDocument>();
        }, Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<PluginManager>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
    }

    public static void RegisterServices(IRegistrator registrator, DiscoveredPlugin plugin)
    {
        foreach (var capability in plugin.Capabilities)
        {
            AddPluginService(registrator, capability.GetType(), capability);

            foreach (var interfaceType in capability.GetType()
                         .GetInterfaces()
                         .Where(static type => type != typeof(IPluginCapability))
                         .Where(static type => typeof(IPluginCapability).IsAssignableFrom(type)))
            {
                AddPluginService(registrator, interfaceType, capability);
            }
        }
    }

    public static void RegisterServices(IRegistrator registrator, IReadOnlyList<DiscoveredPlugin> plugins)
    {
        registrator.RegisterInstance<IReadOnlyCollection<DiscoveredPlugin>>(plugins,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterInstance<IReadOnlyDictionary<string, DiscoveredPlugin>>(
            plugins.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase),
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        foreach (var plugin in plugins)
        {
            RegisterServices(registrator, plugin);
        }
    }

    public static IContainer CreatePluginSubcontainer(
        DependencyInjection.Container runtimeServiceProvider,
        IReadOnlyList<DiscoveredPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(runtimeServiceProvider);
        ArgumentNullException.ThrowIfNull(plugins);

        return runtimeServiceProvider.CreateSubcontainer(registrator => RegisterServices(registrator, plugins));
    }

    private static IRegistrator AddPluginService(IRegistrator registrator, Type serviceType, object serviceInstance)
    {
        registrator.RegisterInstance(serviceType, serviceInstance,
            ifAlreadyRegistered: IfAlreadyRegistered.AppendNewImplementation);
        registrator.RegisterInstance(serviceType, serviceInstance, serviceKey: serviceType,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        return registrator;
    }
}