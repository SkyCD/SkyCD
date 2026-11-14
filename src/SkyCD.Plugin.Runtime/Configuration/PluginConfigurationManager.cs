using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Runtime.Managers;

namespace SkyCD.Plugin.Runtime.Configuration;

/// <summary>
/// Routes configuration requests to the plugin that contributes a configuration surface
/// and persists the settings accepted by the user.
/// </summary>
public sealed class PluginConfigurationManager(PluginManager pluginManager, PluginConfigurationStore store)
{
    /// <summary>
    /// Gets the plugins that currently expose a configuration surface.
    /// </summary>
    public IReadOnlyList<PluginConfigurationRegistration> GetRegistrations()
    {
        return pluginManager.Plugins
            .Select(static plugin => (
                Plugin: plugin,
                Capability: plugin.Capabilities.OfType<IPluginConfigurationCapability>().FirstOrDefault()))
            .Where(static entry => entry.Capability is not null)
            .Select(static entry => new PluginConfigurationRegistration(
                entry.Plugin.Id,
                entry.Plugin.Name,
                entry.Capability!.Configuration))
            .OrderBy(static registration => registration.PluginName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Gets whether the given plugin exposes a configuration surface.
    /// </summary>
    public bool SupportsConfiguration(string? pluginId)
    {
        return ResolveCapability(pluginId) is not null;
    }

    /// <summary>
    /// Opens the configuration surface of the given plugin and persists the accepted settings.
    /// Canceled flows never change the persisted settings.
    /// </summary>
    public async Task<PluginConfigurationOutcome> ConfigureAsync(
        string pluginId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var capability = ResolveCapability(pluginId);
        if (capability is null)
        {
            return new PluginConfigurationOutcome
            {
                Status = PluginConfigurationStatus.NotSupported,
                Error = $"Plugin '{pluginId}' does not provide a configuration surface."
            };
        }

        var request = new PluginConfigurationRequest
        {
            ConfigurationId = capability.Configuration.ConfigurationId,
            CurrentSettings = store.GetSettings(pluginId)
        };

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        PluginConfigurationResult result;
        try
        {
            result = await capability.ConfigureAsync(request, linkedCts.Token);
        }
        catch (OperationCanceledException exception) when (timeoutCts.IsCancellationRequested)
        {
            PluginFailure.Publish(capability.GetType(), "configure", exception);
            return new PluginConfigurationOutcome
            {
                Status = PluginConfigurationStatus.TimedOut,
                Error = $"Configuration of plugin '{pluginId}' timed out."
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PluginFailure.Publish(capability.GetType(), "configure", exception);
            return new PluginConfigurationOutcome
            {
                Status = PluginConfigurationStatus.Failed,
                Error = "The plugin configuration failed."
            };
        }

        if (!result.Success)
        {
            return new PluginConfigurationOutcome
            {
                Status = PluginConfigurationStatus.Failed,
                Error = result.Error ?? "The plugin configuration failed."
            };
        }

        if (result.Canceled)
        {
            return new PluginConfigurationOutcome
            {
                Status = PluginConfigurationStatus.Canceled,
                Settings = store.GetSettings(pluginId)
            };
        }

        store.SaveSettings(pluginId, result.Settings);

        return new PluginConfigurationOutcome
        {
            Status = PluginConfigurationStatus.Succeeded,
            Settings = result.Settings
        };
    }

    private IPluginConfigurationCapability? ResolveCapability(string? pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return null;
        }

        return pluginManager.Plugins
            .Where(plugin => plugin.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static plugin => plugin.Capabilities.OfType<IPluginConfigurationCapability>())
            .FirstOrDefault();
    }
}
