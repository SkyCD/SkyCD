using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Couchbase;
using SkyCD.Couchbase.Repository;
using SkyCD.Plugin.Runtime.Documents;

namespace SkyCD.Plugin.Runtime.Configuration;

/// <summary>
/// Persists plugin configuration settings between host sessions.
/// </summary>
public sealed class PluginConfigurationStore(IRepository<PluginConfigurationDocument> repository)
{
    /// <summary>
    /// Gets the settings currently persisted for the given plugin.
    /// </summary>
    public IReadOnlyDictionary<string, string?> GetSettings(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        var document = repository.Get(pluginId);
        if (document?.Settings is null)
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        return document.Settings
            .Where(static setting => !string.IsNullOrWhiteSpace(setting.Key))
            .ToDictionary(
                static setting => setting.Key,
                static setting => setting.Value?.ToString(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Persists the settings accepted by the user for the given plugin.
    /// </summary>
    public void SaveSettings(string pluginId, IReadOnlyDictionary<string, string?> settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentNullException.ThrowIfNull(settings);

        var stored = settings
            .Where(static setting => !string.IsNullOrWhiteSpace(setting.Key))
            .ToDictionary(
                static setting => setting.Key,
                static setting => (object?)setting.Value,
                StringComparer.OrdinalIgnoreCase);

        repository.Save(pluginId, new PluginConfigurationDocument
        {
            Id = pluginId,
            Settings = stored,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    /// <summary>Persists all validated Options drafts atomically, leaving existing settings intact on failure.</summary>
    public void SaveBatch(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count == 0) return;
        DatabaseGate.InBatch(repository.Collection.Database, () =>
        {
            foreach (var (id, values) in settings) SaveSettings(id, values);
        });
    }

    /// <summary>
    /// Removes the persisted settings of the given plugin.
    /// </summary>
    public void ClearSettings(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        var document = repository.Get(pluginId);
        if (document is null)
        {
            return;
        }

        repository.Save(pluginId, new PluginConfigurationDocument
        {
            Id = pluginId,
            Settings = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase),
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}
