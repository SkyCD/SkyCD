using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Localization;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Host.Modal;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.Factories;

public sealed class ServiceCollectionFactory
{
    public IServiceCollection BuildCommonServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<II18nService, I18nService>();
        services.AddSingleton<FileFormatManager>();
        services.AddSingleton<MenuExtensionManager>();
        services.AddSingleton<ModalExtensionManager>();
        return services;
    }

    public IServiceCollection BuildPluginServiceCollection(DiscoveredPlugin plugin)
    {
        var services = new ServiceCollection();
        services.AddSingleton(plugin);
        services.AddKeyedSingleton<DiscoveredPlugin>(plugin.Id, plugin);

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

        return services;
    }
}
