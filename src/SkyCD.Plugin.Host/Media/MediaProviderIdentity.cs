namespace SkyCD.Plugin.Host.Media;

using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

/// <summary>
/// Identity of a provider that matches a source: plugin, capability type and output schema version.
/// Hosts use it to decide whether cached results are still current without running the provider.
/// </summary>
public sealed record MediaProviderIdentity(
    string PluginId,
    string PluginVersion,
    string Capability,
    string SchemaId,
    int SchemaVersion,
    int Priority,
    IReadOnlyList<MediaMetadataField> SchemaFields);
