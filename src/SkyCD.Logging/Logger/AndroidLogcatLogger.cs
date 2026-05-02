using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.Versioning;

namespace SkyCD.Logging.Logger;

[SupportedOSPlatform(SupportedOsPlatforms.Android)]
internal sealed class AndroidLogcatLogger(string tag, string category) : ILogger
{
    private static readonly MethodInfo? WriteLineMethod = ResolveWriteLineMethod();

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
        WriteLineMethod?.Invoke(null, [Map(logLevel), tag, composed]);
    }

    private static int Map(LogLevel level)
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

    private static MethodInfo? ResolveWriteLineMethod()
    {
        var logType = Type.GetType("Android.Util.Log, Mono.Android");
        return logType?.GetMethod("WriteLine", BindingFlags.Public | BindingFlags.Static, [typeof(int), typeof(string), typeof(string)]);
    }
}
