using Microsoft.Extensions.Logging;
using SkyCD.Logging.Providers;
using System.Runtime.Versioning;

namespace SkyCD.Logging.Drivers;

internal sealed class MacOsUnifiedLogDriver : ILoggingDriver
{
    public bool CanHandleCurrentPlatform => OperatingSystem.IsMacOS() ||
                                            OperatingSystem.IsIOS() ||
                                            OperatingSystem.IsTvOS() ||
                                            OperatingSystem.IsWatchOS();

    [SupportedOSPlatform(SupportedOsPlatforms.MacOs)]
    [SupportedOSPlatform(SupportedOsPlatforms.Ios)]
    [SupportedOSPlatform(SupportedOsPlatforms.TvOs)]
    [SupportedOSPlatform(SupportedOsPlatforms.WatchOs)]
    public void Configure(ILoggingBuilder builder, string subsystem)
    {
        builder.AddProvider(new MacOsUnifiedLogLoggerProvider(subsystem));
    }
}
