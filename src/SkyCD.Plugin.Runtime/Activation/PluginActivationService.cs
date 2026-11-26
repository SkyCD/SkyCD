using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SkyCD.SDK.Capabilities;
using SkyCD.Plugin.Runtime.Discovery;

namespace SkyCD.Plugin.Runtime.Activation;

/// <summary>
/// Drives <see cref="IActivatablePluginCapability"/> instances across plugin discovery passes. Applying the
/// same pass twice is a no-op, and a capability is never started twice without being stopped in between, so
/// re-running Options cannot open a second watcher or capture loop for one plugin.
/// </summary>
public sealed class PluginActivationService
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<ActiveCapability> active = [];
    private readonly ILogger<PluginActivationService>? logger;

    public PluginActivationService(ILogger<PluginActivationService>? logger = null)
    {
        this.logger = logger;
    }

    /// <summary>Gets the number of capabilities currently started by this service.</summary>
    public int ActiveCount
    {
        get
        {
            lock (active)
            {
                return active.Count;
            }
        }
    }

    /// <summary>
    /// Reconciles running capabilities with the plugins of one discovery pass: stopped plugins are
    /// deactivated, newly discovered capabilities are activated and unchanged instances are kept running.
    /// </summary>
    public async Task ApplyAsync(IReadOnlyCollection<DiscoveredPlugin> plugins,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var desired = plugins
                .SelectMany(static plugin => plugin.Capabilities.OfType<IActivatablePluginCapability>()
                    .Select(capability => new ActiveCapability(plugin.Id, capability)))
                .ToArray();

            ActiveCapability[] removed;
            lock (active)
            {
                removed = active
                    .Where(entry => !desired.Any(candidate => ReferenceEquals(candidate.Capability, entry.Capability)))
                    .ToArray();
                foreach (var entry in removed)
                {
                    active.Remove(entry);
                }
            }

            foreach (var entry in removed)
            {
                await DeactivateAsync(entry, cancellationToken).ConfigureAwait(false);
            }

            foreach (var entry in desired)
            {
                lock (active)
                {
                    if (active.Any(existing => ReferenceEquals(existing.Capability, entry.Capability)))
                    {
                        continue;
                    }

                    active.Add(entry);
                }

                try
                {
                    await entry.Capability.ActivateAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    RemoveFromActive(entry);
                    throw;
                }
                catch (Exception exception)
                {
                    RemoveFromActive(entry);
                    logger?.LogWarning(exception,
                        "Plugin '{PluginId}' capability '{Capability}' failed to activate.",
                        entry.PluginId, entry.Capability.GetType().FullName);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Stops every started capability, for example when the host shuts down.</summary>
    public async Task DeactivateAllAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ActiveCapability[] running;
            lock (active)
            {
                running = active.ToArray();
                active.Clear();
            }

            foreach (var entry in running)
            {
                await DeactivateAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task DeactivateAsync(ActiveCapability entry, CancellationToken cancellationToken)
    {
        try
        {
            await entry.Capability.DeactivateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception,
                "Plugin '{PluginId}' capability '{Capability}' failed to deactivate.",
                entry.PluginId, entry.Capability.GetType().FullName);
        }
    }

    private void RemoveFromActive(ActiveCapability entry)
    {
        lock (active)
        {
            active.Remove(entry);
        }
    }

    private sealed record ActiveCapability(string PluginId, IActivatablePluginCapability Capability);
}
