using Microsoft.Extensions.Logging;
using SkyCD.Logging.Logger;
using System.Runtime.Versioning;

namespace SkyCD.Logging.Providers;

[SupportedOSPlatform(SupportedOsPlatforms.Android)]
internal sealed class AndroidLogcatLoggerProvider(string tag) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        categoryName = string.IsNullOrWhiteSpace(categoryName) ? "default" : categoryName;
        return new AndroidLogcatLogger(tag, categoryName);
    }

    public void Dispose()
    {
    }
}
