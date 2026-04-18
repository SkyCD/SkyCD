using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Host.FileFormats;

/// <summary>
/// Host-facing representation of an available plugin file format route.
/// </summary>
public sealed record FileFormatRoute(
    string PluginId,
    string FormatId,
    string DisplayName,
    IReadOnlyCollection<string> Extensions,
    bool CanRead,
    bool CanWrite,
    string? MimeType);
