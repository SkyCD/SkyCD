using System;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SkyCD.Logging.Helpers;

namespace SkyCD.Logging.Logger;

[SupportedOSPlatform(SupportedOsPlatforms.Android)]
internal sealed class AndroidLogcatLogger<TCategoryName>(string tag, string category) : ILogger<TCategoryName>
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
        WritePlatformLog(logLevel, tag, composed);
    }

    private static void WritePlatformLog(LogLevel level, string tag, string message)
    {
        AndroidInteropHelper.WriteLogLine(MapPriority(level), tag, message);
    }

    private static int MapPriority(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => 2,
            LogLevel.Debug => 3,
            LogLevel.Information => 4,
            LogLevel.Warning => 5,
            LogLevel.Error => 6,
            LogLevel.Critical => 7,
            _ => 4
        };
    }
}
