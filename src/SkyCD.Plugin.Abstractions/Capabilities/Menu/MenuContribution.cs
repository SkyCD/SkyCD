namespace SkyCD.Plugin.Abstractions.Capabilities.Menu;

/// <summary>
///     Describes a host menu contribution exposed by a plugin.
/// </summary>
public sealed record MenuContribution(
    string CommandId,
    string Title,
    string Location,
    int Order = 0);