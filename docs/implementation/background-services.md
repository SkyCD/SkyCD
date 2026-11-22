# In-process background services

SkyCD runs long-lived work (the MCP server today, live synchronization later) inside the
desktop process. These are not Windows services or system daemons: catalog data, plugin state
and MCP tool bridging belong to the signed-in user and the running application.

`SkyCD.Core.BackgroundServices.IAppBackgroundService` defines the lifecycle:

```csharp
public interface IAppBackgroundService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

`AppBackgroundServiceHost` owns the registered services. It starts them in registration order
on a background context, and on shutdown stops them in reverse order with a bounded total drain
timeout (5 seconds by default). Start and stop failures are logged per service and never prevent
application exit. The host is drained from `App.OnShutdownRequested` before the DI container is
disposed; `App.Exit` remains a last-resort cleanup path.

## Adding a service

1. Implement `IAppBackgroundService`. Keep `StartAsync` non-blocking (do not spin or sleep on the
   UI thread) and make `StopAsync` honor the cancellation token.
2. Register the singleton in `App.CreateAppServiceProvider` and pass it to the
   `AppBackgroundServiceHost` registration:

```csharp
registrator.Register<LiveSyncService>(Reuse.Singleton);
registrator.RegisterDelegate<AppBackgroundServiceHost>(static resolver =>
    new AppBackgroundServiceHost(
        [resolver.Resolve<McpServerHost>(), resolver.Resolve<LiveSyncService>()],
        resolver.Resolve<ILogger<AppBackgroundServiceHost>>()), Reuse.Singleton);
```

Order in that array is start order; stop order is its reverse. No shutdown code changes are needed.

## When not to use it

- Do not model OS services, daemons or LocalSystem processes here.
- Do not run work that must continue after the app exits; that needs an out-of-process agent with
  IPC, authentication and single-writer database ownership.
- Do not use it for per-view work that already has an owner (for example the opt-in media sync
  owned by the main window view model); the host is for process-wide services only.
- Do not hide failures: let the service throw or log through `ILogger`, and keep `StopAsync`
  bounded so a stuck listener cannot delay shutdown past the drain timeout.
