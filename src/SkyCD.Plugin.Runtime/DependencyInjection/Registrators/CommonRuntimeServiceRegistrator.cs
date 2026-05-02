using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Host.Modal;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Runtime.DependencyInjection.Registrators;

public sealed class CommonRuntimeServiceRegistrator : IServiceRegistrator
{
    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<FileFormatManager>();
        services.AddSingleton<MenuExtensionManager>();
        services.AddSingleton<ModalExtensionManager>();
    }
}
