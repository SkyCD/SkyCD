using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SkyCD.Logging.Logger;

namespace SkyCD.Logging.Providers;

[SupportedOSPlatform(SupportedOsPlatforms.MacOs)]
[SupportedOSPlatform(SupportedOsPlatforms.Ios)]
[SupportedOSPlatform(SupportedOsPlatforms.TvOs)]
[SupportedOSPlatform(SupportedOsPlatforms.WatchOs)]
internal sealed class MacOsUnifiedLogLoggerProvider(string subsystem) : ILoggerProvider
{
    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "os_log_create")]
    private static extern IntPtr CreateOsLogHandle(string subsystem, string category);

    private readonly ConcurrentDictionary<string, IntPtr> logHandles = new(StringComparer.Ordinal);

    public ILogger CreateLogger(string categoryName)
    {
        var logHandle = logHandles.GetOrAdd(categoryName, static (category, subsystemName) =>
            CreateOsLogHandle(subsystemName, category), subsystem);

        return new MacOsLogLogger(logHandle, categoryName);
    }

    public void Dispose()
    {
    }
}
