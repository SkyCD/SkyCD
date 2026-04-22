namespace SkyCD.Plugin.Abstractions.Capabilities.Cli;

/// <summary>
/// Result contract for plugin CLI handlers.
/// </summary>
public sealed class CliCommandResult
{
    /// <summary>
    /// Indicates whether command execution succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Optional plain-text output written by the command.
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Optional error message when <see cref="Success"/> is <see langword="false"/>.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Optional payload for extension-point continuation.
    /// </summary>
    public object? Payload { get; init; }
}
