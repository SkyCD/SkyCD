using System;
using System.Linq;
using System.Collections.Generic;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Collections;
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

    public static void RegisterServices(IRegistrator registrator, IReadOnlyList<DiscoveredPlugin> plugins)
    {
        registrator.RegisterInstance<IReadOnlyCollection<DiscoveredPlugin>>(plugins,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterInstance<IReadOnlyDictionary<string, DiscoveredPlugin>>(
            plugins.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase),
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        var discoveredPlugins = new DiscoveredPluginCollection();
        discoveredPlugins.AddRange(plugins);
        discoveredPlugins.RegisterPluginServices(registrator);
    }
}
