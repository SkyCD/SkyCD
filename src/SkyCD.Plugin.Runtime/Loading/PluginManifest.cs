namespace SkyCD.Plugin.Runtime.Loading;

/// <summary>
/// Manifest metadata loaded from plugin.json.
/// </summary>
public sealed class PluginManifest
{
    public required string Id { get; init; }

    public required string Version { get; init; }

    public required string MinHostVersion { get; init; }

    public required string Assembly { get; init; }

    public IReadOnlyCollection<string> Capabilities { get; init; } = [];
}
