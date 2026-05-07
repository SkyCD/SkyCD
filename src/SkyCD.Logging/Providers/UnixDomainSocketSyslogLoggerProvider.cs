using System.IO;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using SkyCD.Logging.Helpers;
using SkyCD.Logging.Logger;

namespace SkyCD.Logging.Providers;

[SupportedOSPlatform(SupportedOsPlatforms.Linux)]
[SupportedOSPlatform(SupportedOsPlatforms.FreeBsd)]
internal sealed class UnixDomainSocketSyslogLoggerProvider(string appName) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return LoggerCategoryTypeHelper.CreateGenericLogger<UnixDomainSocketSyslogLogger<object>>(categoryName, appName, categoryName, SocketPath);
    }

    public void Dispose()
    {
    }

    private static string? SocketPath
    {
        get
        {
            if (field is not null)
            {
                return field == string.Empty ? null : field;
            }

            var candidates = new[] { "/dev/log", "/var/run/log" };
            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                return field = candidate;
            }

            field = "";

            return null;
        }
    }
}
