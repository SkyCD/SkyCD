namespace SkyCD.Plugin.Abstractions.Capabilities.Cli;

/// <summary>
/// Contribution type for a CLI plugin entry.
/// </summary>
public enum CliContributionType
{
    /// <summary>
    /// Standalone command callable directly from CLI.
    /// </summary>
    Command = 0,

    /// <summary>
    /// Extension hook attached to a host command pipeline.
    /// </summary>
    Extension = 1
}
