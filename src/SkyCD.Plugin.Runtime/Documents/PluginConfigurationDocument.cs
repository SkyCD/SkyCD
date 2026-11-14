using System;
using System.Collections.Generic;
using SkyCD.Couchbase.Attributes;
using SkyCD.Plugin.Runtime.Repositories;

namespace SkyCD.Plugin.Runtime.Documents;

/// <summary>
/// Persisted settings of a single plugin configuration surface.
/// </summary>
[CouchbaseDocument("plugin-configurations", typeof(PluginConfigurationRepository))]
public sealed class PluginConfigurationDocument
{
    public string Id { get; set; } = string.Empty;

    public Dictionary<string, object?> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset UpdatedAt { get; set; }
}
