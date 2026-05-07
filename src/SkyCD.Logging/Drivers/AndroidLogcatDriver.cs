using System;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SkyCD.Logging.Providers;

namespace SkyCD.Logging.Drivers;

internal sealed class AndroidLogcatDriver : ILoggingDriver
{
    public bool CanHandleCurrentPlatform => OperatingSystem.IsAndroid();

    [SupportedOSPlatform(SupportedOsPlatforms.Android)]
    public void Configure(ILoggingBuilder builder, string subsystem)
    {
        builder.AddProvider(new AndroidLogcatLoggerProvider(subsystem));
    }
}
