using System;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SkyCD.Logging.Helpers;

namespace SkyCD.Logging.Logger;

[SupportedOSPlatform(SupportedOsPlatforms.MacOs)]
[SupportedOSPlatform(SupportedOsPlatforms.Ios)]
[SupportedOSPlatform(SupportedOsPlatforms.TvOs)]
[SupportedOSPlatform(SupportedOsPlatforms.WatchOs)]
internal sealed class MacOsLogLogger<TCategoryName>(IntPtr log, string category) : ILogger<TCategoryName>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (exception is not null)
        {
            message = $"{message} {exception}";
        }

        var composed = $"{category} [{eventId.Id}] {message}";
        AppleInteropHelper.WriteAppleLogMessage(log, Map(logLevel), "%{public}s", __arglist(composed));
    }

    private static byte Map(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => 0,
            LogLevel.Debug => 0,
            LogLevel.Information => 1,
            LogLevel.Warning => 16,
            LogLevel.Error => 17,
            LogLevel.Critical => 17,
            _ => 1
        };
    }
}
