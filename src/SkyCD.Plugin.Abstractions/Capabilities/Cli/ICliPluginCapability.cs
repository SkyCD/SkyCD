namespace SkyCD.Plugin.Abstractions.Capabilities.Cli;

/// <summary>
/// CLI capability contract for plugin command contribution and execution.
/// </summary>
public interface ICliPluginCapability : IPluginCapability
{
    /// <summary>
    /// Gets CLI command and extension contributions.
    /// </summary>
    IReadOnlyCollection<CliCommandContribution> GetCliContributions();

    /// <summary>
    /// Executes a contributed command or extension hook.
    /// </summary>
    Task<CliCommandResult> ExecuteCliCommandAsync(CliCommandContext context, CancellationToken cancellationToken = default);
}
