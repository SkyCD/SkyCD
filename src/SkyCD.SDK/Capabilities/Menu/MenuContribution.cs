namespace SkyCD.SDK.Capabilities.Menu;

/// <summary>
/// Describes a host menu contribution exposed by a plugin. <paramref name="RequiresLocalFile"/> marks a
/// contribution that only makes sense for one selected local file; the host keeps it disabled for folders,
/// network entries, media entries and links.
/// </summary>
public sealed record MenuContribution(
    string CommandId,
    string Title,
    string Location,
    int Order = 0,
    bool RequiresLocalFile = false);
