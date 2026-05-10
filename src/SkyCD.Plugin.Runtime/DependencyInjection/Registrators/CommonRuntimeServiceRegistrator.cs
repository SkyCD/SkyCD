using DryIoc;
using Microsoft.Extensions.Logging;
using SkyCD.Logging;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.Host.Modal;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Runtime.DependencyInjection.Registrators;

public sealed class CommonRuntimeServiceRegistrator : IServiceRegistrator
{
    public void RegisterServices(IRegistrator registrator)
    {
        registrator.Register<ILoggerFactory, PlatformLoggerFactory>(Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register(typeof(ILogger<>), typeof(Logger<>), reuse: Reuse.Transient,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<FileFormatManager>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<MenuExtensionManager>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<ModalExtensionManager>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
    }
}