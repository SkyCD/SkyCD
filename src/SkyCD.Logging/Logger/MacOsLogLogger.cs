using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SkyCD.Logging.Logger;

[SupportedOSPlatform(SupportedOsPlatforms.MacOs)]
[SupportedOSPlatform(SupportedOsPlatforms.Ios)]
[SupportedOSPlatform(SupportedOsPlatforms.TvOs)]
[SupportedOSPlatform(SupportedOsPlatforms.WatchOs)]
internal sealed class MacOsLogLogger(IntPtr log, string category) : ILogger
{
    [DllImport("/usr/lib/libSystem.dylib", EntryPoint = "os_log_with_type", CallingConvention = CallingConvention.Cdecl)]
    private static extern void WriteOsLogMessage(IntPtr osLog, byte type, string format, __arglist);

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
        WriteOsLogMessage(log, Map(logLevel), "%{public}s", __arglist(composed));
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
