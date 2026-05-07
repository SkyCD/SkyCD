using System;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SkyCD.Logging.Logger;

[SupportedOSPlatform(SupportedOsPlatforms.Linux)]
[SupportedOSPlatform(SupportedOsPlatforms.FreeBsd)]
internal sealed class UnixDomainSocketSyslogLogger<TCategoryName>(string appName, string category, string? socketPath) : ILogger<TCategoryName>
{
    private readonly int processId = Environment.ProcessId;

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

        var payload = BuildSyslogPayload(logLevel, eventId, message);
        SendToSyslogSocket(payload);
    }

    private string BuildSyslogPayload(LogLevel logLevel, EventId eventId, string message)
    {
        const int facilityUser = 1;
        var severity = MapSeverity(logLevel);
        var priority = (facilityUser * 8) + severity;
        return $"<{priority}>{appName}[{processId}]: {category} [{eventId.Id}] {message.Replace('\n', ' ')}";
    }

    private void SendToSyslogSocket(string payload)
    {
        if (string.IsNullOrWhiteSpace(socketPath))
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(payload);
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
            var endpoint = new UnixDomainSocketEndPoint(socketPath);
            socket.SendTo(bytes, endpoint);
        }
        catch (SocketException)
        {
            // Ignore socket send failures for logging.
        }
    }

    private static int MapSeverity(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => 7,
            LogLevel.Debug => 7,
            LogLevel.Information => 6,
            LogLevel.Warning => 4,
            LogLevel.Error => 3,
            LogLevel.Critical => 2,
            _ => 6
        };
    }
}
