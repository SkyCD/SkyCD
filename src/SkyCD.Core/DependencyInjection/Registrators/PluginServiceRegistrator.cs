using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.Extensions.Logging;
using SkyCD.Core.Plugins.Installation;
using SkyCD.Core.Versioning;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Abstractions.Capabilities.Volume;
using SkyCD.Plugin.Host.AudioCd;
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
    /// <summary>Overrides local application data for the catalog plugin root, staging area and receipts.</summary>
    public const string PluginStateDirectoryEnvironmentVariable = "SKYCD_PLUGIN_STATE_DIRECTORY";

    /// <summary>
    /// Applies every pending catalog plugin operation. Hosts call it once per process, before the first
    /// <c>PluginManager.Discover</c>, so a staged install or update becomes visible to assembly discovery.
    /// </summary>
    public static async Task<IReadOnlyList<PluginInstallResult>> ApplyPendingOperationsBeforeDiscoveryAsync(
        IPluginPackageInstaller installer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installer);
        return await installer.ApplyPendingAsync(cancellationToken).ConfigureAwait(false);
    }

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

        // Catalog plugin installs stage outside the discovery root and activate on the next startup, before
        // PluginManager.Discover enumerates the plugin root.
        registrator.RegisterDelegate<IPluginPackageInstaller>(static resolver =>
            new PluginInstallService(
                CreatePluginInstallLayout(resolver.Resolve<HostVersionProvider>()),
                resolver.Resolve<HostVersionProvider>().Current,
                resolver.Resolve<ILogger<PluginInstallService>>()),
            Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterDelegate<IPluginInstallStateSource>(
            resolver => (IPluginInstallStateSource)resolver.Resolve<IPluginPackageInstaller>(),
            Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        registrator.Register<PluginManager>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
    }

    /// <summary>
    /// Catalog plugin root and hidden staging root defaults. Staging lives in the local application data state
    /// folder and never below the plugin root, so assembly discovery cannot see downloaded packages.
    /// </summary>
    public static PluginInstallLayout CreatePluginInstallLayout(HostVersionProvider? hostVersionProvider = null)
    {
        var stateRoot = Environment.GetEnvironmentVariable(PluginStateDirectoryEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(stateRoot))
        {
            stateRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SkyCD",
                "plugin-state");
        }

        var hostVersion = hostVersionProvider?.Current.ToString() ?? "0.0.0";
        return new PluginInstallLayout(
            Path.Combine(stateRoot, "plugins"),
            Path.Combine(stateRoot, "staging", hostVersion));
    }

    public static void RegisterServices(IRegistrator registrator, IReadOnlyList<DiscoveredPlugin> plugins, OAuthService oauthService,
        PluginConfigurationStore? configurationStore = null)
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
            plugin.Capabilities.Select(capability => new MediaProvider(plugin.Id, plugin.Version.ToString(), capability))),
            settingsSource: configurationStore is null ? null : configurationStore.GetSettings);
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

        // Capture-time settings are read from the configuration store so a persisted change applies to the
        // next scan without restarting the host.
        registrator.RegisterDelegate<AudioCdExtensionManager>(resolver => new AudioCdExtensionManager(
                plugins.SelectMany(plugin => plugin.Capabilities.OfType<IAudioCdMetadataCapability>()
                    .Select(capability => new AudioCdProvider(plugin.Id, plugin.Version.ToString(), capability))),
                resolver.Resolve<PluginConfigurationStore>().GetSettings),
            Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterDelegate<IAudioCdMetadataProvider>(
            resolver => resolver.Resolve<AudioCdExtensionManager>(),
            Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

        registrator.Register<FileFormatManager>(Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<MenuExtensionManager>(Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
    }
}
