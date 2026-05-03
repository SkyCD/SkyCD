using Microsoft.Extensions.Logging;
using SkyCD.Logging.Helpers;
using SkyCD.Logging.Logger;
using System.Runtime.Versioning;

namespace SkyCD.Logging.Providers;

[SupportedOSPlatform(SupportedOsPlatforms.Android)]
internal sealed class AndroidLogcatLoggerProvider(string fallbackTag) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        var logCategory = ResolveLogCategory(categoryName);
        return LoggerCategoryTypeHelper.CreateGenericLogger<AndroidLogcatLogger<object>>(categoryName, logCategory, categoryName);
    }

    public void Dispose()
    {
    }

    private string ResolveLogCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return fallbackTag;
        }

        var lastDotIndex = categoryName.LastIndexOf('.');
        if (lastDotIndex >= 0 && lastDotIndex < categoryName.Length - 1)
        {
            return categoryName[(lastDotIndex + 1)..];
        }

        return categoryName;
    }
}
