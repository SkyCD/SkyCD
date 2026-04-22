namespace SkyCD.Plugin.Abstractions.Capabilities.Cli;

/// <summary>
/// Runtime context passed to plugin CLI handlers.
/// </summary>
public sealed class CliCommandContext
{
    /// <summary>
    /// Gets the command path being executed.
    /// </summary>
    public required string CommandPath { get; init; }

    /// <summary>
    /// Gets contribution identifier selected by host.
    /// </summary>
    public required string CommandId { get; init; }

    /// <summary>
    /// Gets raw command arguments not parsed by host command parser.
    /// </summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>
    /// Gets whether machine-readable JSON output was requested.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Gets extension payload for command extension points.
    /// </summary>
    public object? Payload { get; init; }

    /// <summary>
    /// Gets host API surface available to CLI plugins.
    /// </summary>
    public required IHostCliApi HostApi { get; init; }
}
