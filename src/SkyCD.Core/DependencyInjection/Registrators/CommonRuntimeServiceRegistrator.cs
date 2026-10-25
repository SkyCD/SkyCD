using DryIoc;
using Microsoft.Extensions.Logging;
using SkyCD.Logging;
using SkyCD.Plugin.Host.Modal;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Core.Versioning;
using System;
using System.IO;
using System.Net.Http;
using SkyCD.Core.OAuth;

namespace SkyCD.Core.DependencyInjection.Registrators;

public sealed class CommonRuntimeServiceRegistrator
{
    public void RegisterServices(IRegistrator registrator)
    {
        registrator.RegisterDelegate<IOAuthDataProtector>(static _ => OAuthDataProtectorFactory.Create(), Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterDelegate<IOAuthBrowser>(static _ => new SystemOAuthBrowser(), Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterDelegate<IOAuthTokenStore>(static resolver =>
            new ProtectedFileOAuthTokenStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SkyCD", "OAuth"), resolver.Resolve<IOAuthDataProtector>()), Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.RegisterDelegate<OAuthService>(static resolver => new OAuthService(
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) },
            resolver.Resolve<IOAuthTokenStore>(), resolver.Resolve<IOAuthBrowser>(), TimeProvider.System), Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<ILoggerFactory, PlatformLoggerFactory>(Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register(typeof(ILogger<>), typeof(Logger<>), reuse: Reuse.Transient,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<FileFormatManager>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<ModalExtensionManager>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        registrator.Register<HostVersionProvider>(Reuse.Singleton,
            ifAlreadyRegistered: IfAlreadyRegistered.Replace);
    }
}


