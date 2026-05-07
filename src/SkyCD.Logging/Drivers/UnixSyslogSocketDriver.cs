using System;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SkyCD.Logging.Providers;

namespace SkyCD.Logging.Drivers;

internal sealed class UnixSyslogSocketDriver : ILoggingDriver
{
    public bool CanHandleCurrentPlatform => OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD();

    [SupportedOSPlatform(SupportedOsPlatforms.Linux)]
    [SupportedOSPlatform(SupportedOsPlatforms.FreeBsd)]
    public void Configure(ILoggingBuilder builder, string subsystem)
    {
        builder.AddProvider(new UnixDomainSocketSyslogLoggerProvider(subsystem));
    }
}
