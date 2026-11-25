using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Runtime.Configuration;

/// <summary>Creates isolated Options drafts and persists settings only when the host accepts them.</summary>
public sealed class PluginConfigurationManager(PluginManager pluginManager, PluginConfigurationStore store)
{
    /// <summary>Lists enabled configuration plugins, or all available plugins for pending Options toggles.</summary>
    public IReadOnlyList<PluginConfigurationRegistration> GetRegistrations(bool includeDisabled = false)
    {
        var enabledIds = pluginManager.GetPluginDescriptors().Where(p => p.IsEnabled)
            .Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var registrations = new List<PluginConfigurationRegistration>();
        foreach (var plugin in pluginManager.AvailablePlugins)
        {
            if (!includeDisabled && !enabledIds.Contains(plugin.Id))
            {
                continue;
            }

            var capability = plugin.Capabilities.OfType<IPluginConfigurationCapability>().FirstOrDefault();
            if (capability is null)
            {
                continue;
            }

            try
            {
                registrations.Add(new PluginConfigurationRegistration(plugin.Id, plugin.Name,
                    capability.Configuration));
            }
            catch (Exception exception)
            {
                // A stale plugin can throw binding errors while reading its own descriptor; isolate it so
                // the remaining plugins still register and the Options dialog stays usable.
                PluginFailure.Publish(capability.GetType(), "configure", exception);
            }
        }

        return registrations
            .OrderBy(p => p.PluginName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.PluginId, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Loads stored settings into a new draft without creating UI or persisting anything.</summary>
    public Task<PluginConfigurationOutcome> CreatePageAsync(string pluginId, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var capability = ResolveCapability(pluginId);
        if (capability is null)
            return Task.FromResult(new PluginConfigurationOutcome
            {
                Status = PluginConfigurationStatus.NotSupported,
                Error = $"Plugin '{pluginId}' does not provide a configuration surface."
            });

        return RunAsync(pluginId, capability.GetType(), async token =>
        {
            var task = capability.CreatePageAsync(new PluginConfigurationRequest
            {
                ConfigurationId = capability.Configuration.ConfigurationId,
                CurrentSettings = store.GetSettings(pluginId)
            }, token);
            try
            {
                var page = await task.WaitAsync(token);
                return new PluginConfigurationOutcome { Status = PluginConfigurationStatus.Succeeded, Page = page };
            }
            catch (OperationCanceledException)
            {
                // An uncooperative asynchronous plugin can finish after the host timeout/close.
                _ = DisposeAbandonedPageAsync(task);
                throw;
            }
        }, timeout, cancellationToken);
    }

    /// <summary>Validates and snapshots a draft. Does not persist or apply values.</summary>
    public Task<PluginConfigurationOutcome> AcceptPageAsync(string pluginId, PluginConfigurationPage page,
        TimeSpan timeout, CancellationToken cancellationToken = default) =>
        RunAsync(pluginId, ResolveCapability(pluginId)?.GetType() ?? page.GetType(), async token =>
        {
            var result = await page.AcceptAsync(token).WaitAsync(token);
            return new PluginConfigurationOutcome
            {
                Status = !result.Success ? PluginConfigurationStatus.Failed : result.Canceled
                    ? PluginConfigurationStatus.Canceled : PluginConfigurationStatus.Succeeded,
                Settings = new Dictionary<string, string?>(result.Settings, StringComparer.OrdinalIgnoreCase),
                Error = result.Success ? null : result.Error ?? "The plugin configuration failed."
            };
        }, timeout, cancellationToken);

    /// <summary>
    /// Persists an already validated batch. Headless hosts can also use the store directly.
    /// Consumer failures are reported independently: persisted values remain authoritative.
    /// </summary>
    public void SaveSettings(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> settings)
    {
        store.SaveBatch(settings);

        foreach (var plugin in pluginManager.AvailablePlugins)
        {
            if (!settings.TryGetValue(plugin.Id, out var values)) continue;
            foreach (var consumer in plugin.Capabilities.OfType<IPluginSettingsConsumer>())
            {
                try { consumer.ApplySettings(values); }
                catch (Exception exception) { PluginFailure.Publish(consumer.GetType(), "configure", exception); }
            }
        }
    }

    /// <summary>Refreshes newly discovered enabled consumers after the host rebuilds runtime registrations.</summary>
    public void RefreshConsumers()
    {
        foreach (var plugin in pluginManager.Plugins)
        {
            var values = store.GetSettings(plugin.Id);
            foreach (var consumer in plugin.Capabilities.OfType<IPluginSettingsConsumer>())
            {
                try { consumer.ApplySettings(values); }
                catch (Exception exception) { PluginFailure.Publish(consumer.GetType(), "configure", exception); }
            }
        }
    }

    private static async Task<PluginConfigurationOutcome> RunAsync(string pluginId, Type type,
        Func<CancellationToken, Task<PluginConfigurationOutcome>> operation, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try { return await operation(linkedCts.Token); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException exception) when (timeoutCts.IsCancellationRequested)
        {
            PluginFailure.Publish(type, "configure", exception);
            return new PluginConfigurationOutcome
            {
                Status = PluginConfigurationStatus.TimedOut,
                Error = $"Configuration of plugin '{pluginId}' timed out."
            };
        }
        catch (Exception exception)
        {
            PluginFailure.Publish(type, "configure", exception);
            return new PluginConfigurationOutcome
            {
                Status = PluginConfigurationStatus.Failed,
                Error = "The plugin configuration failed."
            };
        }
    }

    private static async Task DisposeAbandonedPageAsync(Task<PluginConfigurationPage> task)
    {
        try { (await task).Dispose(); }
        catch (Exception exception) { PluginFailure.Publish(typeof(PluginConfigurationPage), "configure", exception); }
    }

    private IPluginConfigurationCapability? ResolveCapability(string? pluginId) => pluginManager.AvailablePlugins
        .Where(p => p.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
        .SelectMany(p => p.Capabilities.OfType<IPluginConfigurationCapability>()).FirstOrDefault();
}
