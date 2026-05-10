using System;
using System.Collections.Generic;
using System.Linq;
using DryIoc;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Cli.DependencyInjection;

public sealed class CliRuntimeServiceRegistrator : IServiceRegistrator
{
    public void RegisterServices(IRegistrator registrator)
    {
        ArgumentNullException.ThrowIfNull(registrator);

        registrator.Register<CliContributionRegistry>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterInstance<IReadOnlyCollection<DiscoveredPlugin>>([], ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterInstance<IReadOnlyDictionary<string, DiscoveredPlugin>>(
            new Dictionary<string, DiscoveredPlugin>(StringComparer.OrdinalIgnoreCase),
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
    }

    public static void RegisterPluginServices(
        IRegistrator registrator,
        IReadOnlyList<DiscoveredPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(registrator);
        ArgumentNullException.ThrowIfNull(plugins);

        var pluginById = plugins.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        registrator.RegisterInstance<IReadOnlyCollection<DiscoveredPlugin>>(plugins, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterInstance<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.AddPluginRegistrator(plugins);
    }
}
