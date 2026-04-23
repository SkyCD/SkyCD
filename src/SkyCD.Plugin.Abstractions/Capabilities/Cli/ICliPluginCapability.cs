namespace SkyCD.Plugin.Abstractions.Capabilities.Cli;

/// <summary>
/// CLI capability contract for plugin command contribution and execution.
/// </summary>
public interface ICliPluginCapability : IPluginCapability
{
    /// <summary>
    /// Gets CLI command or extension contribution.
    /// </summary>
    CliCommandContribution Command { get; }

    /// <summary>
    /// Executes a contributed command or extension hook.
    /// </summary>
    Task<CliCommandResult> ExecuteCliCommandAsync(CliCommandContext context, CancellationToken cancellationToken = default);
}
