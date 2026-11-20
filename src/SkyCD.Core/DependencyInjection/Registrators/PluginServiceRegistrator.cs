using System;
using System.Linq;
using System.Collections.Generic;
using DryIoc;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Abstractions.Capabilities.Volume;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Host.Volume;
using SkyCD.Plugin.Runtime.Collections;
using SkyCD.Plugin.Runtime.Configuration;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Documents;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Core.OAuth;
using SkyCD.Plugin.Abstractions.OAuth;
using SkyCD.Plugin.Abstractions;

namespace SkyCD.Core.DependencyInjection.Registrators;

public sealed class PluginServiceRegistrator
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
        registrator.RegisterDelegate<IRepository<PluginConfigurationDocument>>(static resolver =>
        {
            var repositoryManager = resolver.Resolve<RepositoryManager>();
            return (IRepository<PluginConfigurationDocument>)repositoryManager.For<PluginConfigurationDocument>();
        }, Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<PluginConfigurationStore>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<PluginConfigurationManager>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<PluginManager>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
    }

    public static void RegisterServices(IRegistrator registrator, IReadOnlyList<DiscoveredPlugin> plugins, OAuthService oauthService)
    {
        foreach (var plugin in plugins.Where(static plugin => plugin is not null))
        {
            foreach (var client in plugin.Capabilities.OfType<IOAuthClient>())
            {
                try { client.InitializeOAuth(oauthService.ForPlugin(plugin.Id)); }
                catch (Exception exception) { PluginFailure.Publish(client.GetType(), "plugin", exception); }
            }
        }

        registrator.RegisterInstance<IReadOnlyCollection<DiscoveredPlugin>>(plugins,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterInstance<IReadOnlyDictionary<string, DiscoveredPlugin>>(
            plugins
                .Where(static plugin => plugin is not null && !string.IsNullOrWhiteSpace(plugin.Id))
                .ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase),
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        var discoveredPlugins = new DiscoveredPluginCollection();
        discoveredPlugins.AddRange(plugins.Where(static plugin => plugin is not null));
        discoveredPlugins.RegisterPluginServices(registrator);

        var mediaManager = new MediaExtensionManager(plugins.SelectMany(plugin =>
            plugin.Capabilities.Select(capability => new MediaProvider(plugin.Id, plugin.Version.ToString(), capability))));
        registrator.RegisterInstance(mediaManager, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        registrator.RegisterInstance(new IndexWorkItemSequence(mediaManager, plugins.SelectMany(plugin =>
                plugin.Capabilities.OfType<IIndexWorkItemProvider>())),
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        registrator.RegisterInstance(new VolumeExtensionManager(plugins.SelectMany(plugin =>
            plugin.Capabilities.OfType<IVolumeMetadataCapability>()
                .Select(capability => new VolumeProvider(plugin.Id, plugin.Version.ToString(), capability)))),
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        registrator.RegisterInstance(new DescriptionExtensionManager(plugins.SelectMany(plugin =>
            plugin.Capabilities.OfType<IGeneratedDescriptionCapability>())),
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        registrator.Register<FileFormatManager>(Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<MenuExtensionManager>(Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
    }
}
