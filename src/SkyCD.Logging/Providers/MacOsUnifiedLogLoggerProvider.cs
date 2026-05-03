using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.Versioning;
using SkyCD.Logging.Helpers;
using SkyCD.Logging.Logger;

namespace SkyCD.Logging.Providers;

[SupportedOSPlatform(SupportedOsPlatforms.MacOs)]
[SupportedOSPlatform(SupportedOsPlatforms.Ios)]
[SupportedOSPlatform(SupportedOsPlatforms.TvOs)]
[SupportedOSPlatform(SupportedOsPlatforms.WatchOs)]
internal sealed class MacOsUnifiedLogLoggerProvider(string subsystem) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, IntPtr> logHandles = new(StringComparer.Ordinal);

    public ILogger CreateLogger(string categoryName)
    {
        var logHandle = logHandles.GetOrAdd(categoryName, static (category, subsystemName) =>
            AppleInteropHelper.CreateAppleLogHandle(subsystemName, category), subsystem);

        return LoggerCategoryTypeHelper.CreateGenericLogger<MacOsLogLogger<object>>(categoryName, logHandle, categoryName);
    }

    public void Dispose()
    {
    }
}
