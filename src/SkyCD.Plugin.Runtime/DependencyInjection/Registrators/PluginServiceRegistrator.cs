using Microsoft.Extensions.DependencyInjection;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Factories;

namespace SkyCD.Plugin.Runtime.DependencyInjection.Registrators;

public sealed class PluginServiceRegistrator : IServiceRegistrator
{
    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<AssembliesListFactory>();
        services.AddSingleton<DiscoveredPluginFactory>();
        services.AddSingleton<PluginDocumentFactory>();
        services.AddSingleton<PluginManager>();
        
        var provider = services.BuildServiceProvider();
        var pluginManager = ActivatorUtilities.CreateInstance<PluginManager>(provider);
        
        foreach (var plugin in pluginManager.Plugins)
        {
            RegisterServices(services, plugin);
        }
    }

    public static void RegisterServices(IServiceCollection services, DiscoveredPlugin plugin)
    {
        foreach (var capability in plugin.Capabilities)
        {
            services.AddPluginService(capability.GetType(), capability);

            foreach (var interfaceType in capability.GetType()
                         .GetInterfaces()
                         .Where(static type => type != typeof(IPluginCapability))
                         .Where(static type => typeof(IPluginCapability).IsAssignableFrom(type)))
            {
                services.AddPluginService(interfaceType, capability);
            }
        }
    }

}
