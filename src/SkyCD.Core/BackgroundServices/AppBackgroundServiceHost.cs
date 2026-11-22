using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SkyCD.Core.BackgroundServices;

/// <summary>
/// Owns the in-process background services: starts them in registration order on a background
/// context and stops them in reverse order with a bounded total drain timeout. Failures are
/// logged and never prevent application exit.
/// </summary>
public sealed class AppBackgroundServiceHost
{
    public static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<IAppBackgroundService> services;
    private readonly ILogger<AppBackgroundServiceHost>? logger;
    private readonly TimeSpan shutdownTimeout;
    private readonly Lock sync = new();
    private readonly List<IAppBackgroundService> startedServices = [];
    private bool startRequested;
    private bool stopRequested;

    public AppBackgroundServiceHost(
        IEnumerable<IAppBackgroundService> services,
        ILogger<AppBackgroundServiceHost>? logger = null,
        TimeSpan? shutdownTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        this.services = services.ToArray();
        this.logger = logger;
        this.shutdownTimeout = shutdownTimeout ?? DefaultShutdownTimeout;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(this.shutdownTimeout, TimeSpan.Zero);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        lock (sync)
        {
            if (startRequested || stopRequested)
            {
                return;
            }

            startRequested = true;
        }

        await Task.Run(async () =>
        {
            foreach (var service in services)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (sync)
                {
                    if (stopRequested)
                    {
                        return;
                    }
                }

                try
                {
                    await service.StartAsync(cancellationToken).ConfigureAwait(false);
                    bool stopImmediately;
                    lock (sync)
                    {
                        stopImmediately = stopRequested;
                        if (!stopImmediately)
                        {
                            startedServices.Add(service);
                        }
                    }

                    // Shutdown began while this service was starting, so it is not part of the stop snapshot.
                    if (stopImmediately)
                    {
                        await StopStartedServiceAsync(service).ConfigureAwait(false);
                        return;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger?.LogError(exception, "Background service {Service} failed to start.", ServiceName(service));
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        IAppBackgroundService[] toStop;
        lock (sync)
        {
            stopRequested = true;
            toStop = startedServices.AsEnumerable().Reverse().ToArray();
            startedServices.Clear();
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(shutdownTimeout);

        foreach (var service in toStop)
        {
            if (timeout.IsCancellationRequested)
            {
                logger?.LogWarning(
                    "Background service shutdown timed out after {Timeout}; {Service} and the remaining services were not stopped.",
                    shutdownTimeout, ServiceName(service));
                break;
            }

            var stopTask = Task.Run(() => service.StopAsync(timeout.Token), CancellationToken.None);
            var completed = await Task.WhenAny(stopTask, Task.Delay(Timeout.Infinite, timeout.Token)).ConfigureAwait(false);
            if (completed != stopTask)
            {
                _ = stopTask.ContinueWith(
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                logger?.LogWarning(
                    "Background service {Service} did not stop within {Timeout}.", ServiceName(service), shutdownTimeout);
                break;
            }

            try
            {
                await stopTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "Background service {Service} failed to stop.", ServiceName(service));
            }
        }
    }

    private async Task StopStartedServiceAsync(IAppBackgroundService service)
    {
        try
        {
            await service.StopAsync(CancellationToken.None).WaitAsync(shutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            logger?.LogWarning(
                "Background service {Service} did not stop within {Timeout}.", ServiceName(service), shutdownTimeout);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Background service {Service} failed to stop.", ServiceName(service));
        }
    }

    private static string ServiceName(IAppBackgroundService service) => service.GetType().FullName ?? service.GetType().Name;
}
