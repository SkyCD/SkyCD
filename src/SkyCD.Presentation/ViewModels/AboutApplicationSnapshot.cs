namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Host facts captured when the About dialog opens. A null count or name means the host could
/// not provide the value, so the matching diagnostic row is omitted instead of fabricated.
/// </summary>
public sealed record AboutApplicationSnapshot(
    string? HostVersion = null,
    int? LoadedPluginCount = null,
    int? EnabledPluginCount = null,
    string? CatalogName = null,
    int? CatalogEntryCount = null);
