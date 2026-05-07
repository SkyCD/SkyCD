using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SkyCD.Logging.Drivers;
using Xunit;

namespace SkyCD.Logging.Tests;

public sealed class PlatformLoggerFactoryTests
{
    [Fact]
    public void CreateLogger_ReturnsLoggerInstance()
    {
        using var factory = new PlatformLoggerFactory("tests.subsystem");

        var logger = factory.CreateLogger("tests.category");

        Assert.NotNull(logger);
    }

    [Fact]
    public void AddProvider_UsesAddedProviderWhenLogging()
    {
        using var factory = new PlatformLoggerFactory("tests.subsystem");
        using var provider = new CapturingLoggerProvider();
        factory.AddProvider(provider);

        var logger = factory.CreateLogger("tests.category");
        logger.LogInformation("hello");

        var message = Assert.Single(provider.Messages);
        Assert.Equal("hello", message);
    }

    [Fact]
    public void PlatformDriver_ResolvesExpectedDriverForCurrentPlatform()
    {
        var property = typeof(PlatformLoggerFactory).GetProperty("PlatformDriver", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(property);

        var driver = property!.GetValue(null);
        Assert.NotNull(driver);

        var expected = GetExpectedDriverType();
        Assert.IsType(expected, driver);
    }

    [Fact]
    public void ResolveSubsystemName_UsesProvidedSubsystem()
    {
        var method = typeof(PlatformLoggerFactory).GetMethod("ResolveSubsystemName", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(null, ["custom.subsystem"]) as string;

        Assert.Equal("custom.subsystem", result);
    }

    [Fact]
    public void ResolveSubsystemName_ReturnsNonEmptyForNull()
    {
        var method = typeof(PlatformLoggerFactory).GetMethod("ResolveSubsystemName", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(null, [null]) as string;

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    private static Type GetExpectedDriverType()
    {
        if (OperatingSystem.IsWindows())
        {
            return typeof(WindowsEventLogDriver);
        }

        if (OperatingSystem.IsMacOS() ||
            OperatingSystem.IsIOS() ||
            OperatingSystem.IsTvOS() ||
            OperatingSystem.IsWatchOS())
        {
            return typeof(MacOsUnifiedLogDriver);
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
        {
            return typeof(UnixSyslogSocketDriver);
        }

        if (OperatingSystem.IsAndroid())
        {
            return typeof(AndroidLogcatDriver);
        }

        return typeof(NoOpLoggingDriver);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName)
        {
            return new CapturingLogger(Messages);
        }

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(List<string> messages) : ILogger
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

            messages.Add(formatter(state, exception));
        }
    }
}
