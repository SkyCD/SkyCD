using System;
using System.Collections.Generic;
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
}
