namespace SkyCD.Plugin.Abstractions.Capabilities.Cli;

/// <summary>
/// Describes a CLI contribution from a plugin.
/// </summary>
/// <param name="CommandPath">
/// Command path. For example: "plugins doctor" or "convert" for an extension point.
/// </param>
/// <param name="CommandId">
/// Stable plugin-local command identifier passed to execution API.
/// </param>
/// <param name="Description">Human-readable description.</param>
/// <param name="ContributionType">Declares whether this is a new command or extension hook.</param>
/// <param name="Priority">Priority for extension execution (higher runs first).</param>
public sealed record CliCommandContribution(
    string CommandPath,
    string CommandId,
    string Description,
    CliContributionType ContributionType = CliContributionType.Command,
    int Priority = 0);
