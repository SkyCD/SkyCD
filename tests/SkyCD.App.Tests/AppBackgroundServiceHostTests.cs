using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SkyCD.Core.BackgroundServices;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class AppBackgroundServiceHostTests
{
    [Fact]
    public async Task StartThenStop_UsesRegistrationOrderAndReverseStopOrder()
    {
        var calls = new List<string>();
        var host = CreateHost([new RecordingService("first", calls), new RecordingService("second", calls)]);

        await host.StartAsync(CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        Assert.Equal(new[] { "start:first", "start:second", "stop:second", "stop:first" }, calls);
    }

    [Fact]
    public async Task Start_ServiceFailure_IsLoggedAndOtherServicesStillStart()
    {
        var logger = new RecordingLogger();
        var started = new RecordingService("healthy", []);
        var host = CreateHost([new FailingStartService(), started], logger);

        await host.StartAsync(CancellationToken.None);

        Assert.True(started.Started);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("failed to start", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Stop_ServiceFailure_IsLoggedAndRemainingServicesStillStop()
    {
        var logger = new RecordingLogger();
        var calls = new List<string>();
        var healthy = new RecordingService("healthy", calls);
        var host = CreateHost([healthy, new FailingStopService()], logger);

        await host.StartAsync(CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        Assert.True(healthy.Stopped);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("failed to stop", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Stop_NeverCompletingService_ReturnsWithinTheShutdownTimeout()
    {
        var host = CreateHost([new NeverStoppingService()], shutdownTimeout: TimeSpan.FromMilliseconds(200));
        await host.StartAsync(CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await host.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"StopAsync took {stopwatch.Elapsed}, which exceeds the bounded drain timeout.");
    }

    [Fact]
    public async Task Stop_WithoutStart_DoesNothing()
    {
        var calls = new List<string>();
        var host = CreateHost([new RecordingService("first", calls)]);

        await host.StopAsync(CancellationToken.None);

        Assert.Empty(calls);
    }

    private static AppBackgroundServiceHost CreateHost(
        IAppBackgroundService[] services,
        RecordingLogger? logger = null,
        TimeSpan? shutdownTimeout = null) =>
        new(services, logger, shutdownTimeout ?? TimeSpan.FromSeconds(5));

    private sealed class RecordingService(string name, List<string> calls) : IAppBackgroundService
    {
        public bool Started { get; private set; }
        public bool Stopped { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            calls.Add($"start:{name}");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stopped = true;
            calls.Add($"stop:{name}");
            return Task.CompletedTask;
        }
    }

    private sealed class FailingStartService : IAppBackgroundService
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("start failed"));

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FailingStopService : IAppBackgroundService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("stop failed"));
    }

    private sealed class NeverStoppingService : IAppBackgroundService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => new TaskCompletionSource().Task;
    }

    private sealed class RecordingLogger : ILogger<AppBackgroundServiceHost>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => EmptyDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
