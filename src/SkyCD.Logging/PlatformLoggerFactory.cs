using Microsoft.Extensions.Logging;
using SkyCD.Logging.Drivers;
using System.Reflection;

namespace SkyCD.Logging;

public sealed class PlatformLoggerFactory : ILoggerFactory
{
    private readonly ILoggerFactory inner;

    public PlatformLoggerFactory(string? subsystem = null)
    {
        var resolvedSubsystem = ResolveSubsystemName(subsystem);

        inner = LoggerFactory.Create(builder =>
        {
            PlatformDriver.Configure(builder, resolvedSubsystem);

            builder.AddDebug();
        });
    }

    private static ILoggingDriver PlatformDriver
    {
        get
        {
            if (field is not null)
            {
                return field;
            }
            
            ILoggingDriver[] drivers =
            [
                new WindowsEventLogDriver(),
                new MacOsUnifiedLogDriver(),
                new UnixSyslogSocketDriver(),
                new AndroidLogcatDriver()
            ];

            foreach (var driver in drivers)
            {
                if (driver.CanHandleCurrentPlatform)
                {
                    field = driver;

                    break;
                }
            }

            field ??= new NoOpLoggingDriver();

            return field;
        }
    }

    private static string ResolveSubsystemName(string? configuredSubsystem)
    {
        if (!string.IsNullOrWhiteSpace(configuredSubsystem))
        {
            return configuredSubsystem;
        }

        var entryName = Assembly.GetEntryAssembly()?.GetName().Name;
        return !string.IsNullOrWhiteSpace(entryName) ? entryName : AppDomain.CurrentDomain.FriendlyName;
    }
    public ILogger CreateLogger(string categoryName)
    {
        return inner.CreateLogger(categoryName);
    }

    public void AddProvider(ILoggerProvider provider)
    {
        inner.AddProvider(provider);
    }

    public void Dispose()
    {
        inner.Dispose();
    }
}
