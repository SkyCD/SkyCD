namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
/// Result payload for file format read operations.
/// </summary>
public sealed class FileFormatReadResult
{
    /// <summary>
    /// Gets whether the operation completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets parsed payload when operation succeeds.
    /// </summary>
    public object? Payload { get; init; }

    /// <summary>
    /// Gets an optional failure message when operation fails.
    /// </summary>
    public string? Error { get; init; }
}
