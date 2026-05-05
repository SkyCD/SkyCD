using System;
using SkyCD.Couchbase.Attributes;
using SkyCD.Plugin.Runtime.Repositories;

namespace SkyCD.Plugin.Runtime.Documents;

[CouchbaseDocument("plugins", typeof(PluginRepository))]
public sealed class PluginDocument
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public PluginAuthorDocument? Author { get; set; }

    public string? ProjectUrl { get; set; }

    public string Version { get; set; } = "1.0.0";

    public PluginConstraintsDocument Constraints { get; set; } = new();

    public string Description { get; set; } = string.Empty;

    public string AssemblyPath { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public bool IsAvailable { get; set; } = true;

    public DateTimeOffset LastDiscoveredAt { get; set; }
}
