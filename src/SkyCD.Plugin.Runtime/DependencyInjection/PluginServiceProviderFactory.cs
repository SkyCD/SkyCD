using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.DependencyInjection;

/// <summary>
/// Builds a process-level service provider from discovered plugin instances.
/// </summary>
public sealed class PluginServiceProviderFactory
{
    public ServiceProvider Build(
        IEnumerable<DiscoveredPlugin> plugins,
        Action<IServiceCollection>? configureHostServices = null)
    {
        var pluginList = plugins.ToList();
        var pluginById = pluginList
            .ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);

        var services = new ServiceCollection();
        services.AddSingleton<IReadOnlyList<DiscoveredPlugin>>(pluginList);
        services.AddSingleton<IReadOnlyCollection<DiscoveredPlugin>>(pluginList);
        services.AddSingleton<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);

        foreach (var plugin in pluginList)
        {
            services.AddSingleton(plugin);
            services.AddKeyedSingleton<DiscoveredPlugin>(plugin.Id, plugin);
            RegisterPluginCapabilities(services, plugin);
        }

        configureHostServices?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static void RegisterPluginCapabilities(IServiceCollection services, DiscoveredPlugin plugin)
    {
        foreach (var capability in plugin.Capabilities)
        {
            services.AddSingleton(typeof(IPluginCapability), capability);
            services.AddKeyedSingleton(typeof(IPluginCapability), typeof(IPluginCapability), capability);
            services.AddSingleton(capability.GetType(), capability);

            foreach (var interfaceType in capability.GetType()
                         .GetInterfaces()
                         .Where(static type => type != typeof(IPluginCapability))
                         .Where(static type => typeof(IPluginCapability).IsAssignableFrom(type)))
            {
                services.AddSingleton(interfaceType, capability);
                services.AddKeyedSingleton(interfaceType, interfaceType, capability);
            }
        }
    }

}
