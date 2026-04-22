namespace SkyCD.Plugin.Abstractions.Capabilities.Cli;

/// <summary>
/// Result contract for plugin CLI handlers.
/// </summary>
public sealed class CliCommandResult
{
    public required bool Success { get; init; }

    public string? Output { get; init; }

    public string? Error { get; init; }

    /// <summary>
    /// Optional payload for extension-point continuation.
    /// </summary>
    public object? Payload { get; init; }
}
