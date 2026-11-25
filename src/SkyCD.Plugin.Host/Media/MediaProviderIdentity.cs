namespace SkyCD.Plugin.Host.Media;

using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

/// <summary>
/// Identity of a provider that matches a source: plugin, capability type and output schema version.
/// Hosts use it to decide whether cached results are still current without running the provider.
/// <paramref name="SettingsRevision"/> is empty when the provider has no persisted settings, so a settings
/// change makes earlier results stale exactly like a schema change would.
/// </summary>
public sealed record MediaProviderIdentity(
    string PluginId,
    string PluginVersion,
    string Capability,
    string SchemaId,
    int SchemaVersion,
    int Priority,
    IReadOnlyList<MediaMetadataField> SchemaFields,
    string SettingsRevision = "");
