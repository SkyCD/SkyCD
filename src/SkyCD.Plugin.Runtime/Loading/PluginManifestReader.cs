using System.Text.Json;

namespace SkyCD.Plugin.Runtime.Loading;

/// <summary>
/// Reads and validates plugin manifest files.
/// </summary>
public sealed class PluginManifestReader
{
    public PluginManifest ReadFromFile(string manifestPath)
    {
        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (manifest is null)
        {
            throw new InvalidOperationException($"Unable to deserialize manifest '{manifestPath}'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Id) ||
            string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.MinHostVersion) ||
            string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            throw new InvalidOperationException($"Manifest '{manifestPath}' is missing required fields.");
        }

        return manifest;
    }
}
