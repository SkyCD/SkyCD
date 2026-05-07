using System;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;

namespace SkyCD.Logging.Drivers;

internal sealed class WindowsEventLogDriver : ILoggingDriver
{
    public bool CanHandleCurrentPlatform => OperatingSystem.IsWindows();

    [SupportedOSPlatform(SupportedOsPlatforms.Windows)]
    public void Configure(ILoggingBuilder builder, string subsystem)
    {
        builder.AddEventLog(new EventLogSettings
        {
            MachineName = Environment.MachineName,
            LogName = "Application",
            SourceName = subsystem
        });
    }
}
