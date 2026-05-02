using Microsoft.Extensions.Logging;
using SkyCD.Logging.Providers;
using System.Runtime.Versioning;

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
