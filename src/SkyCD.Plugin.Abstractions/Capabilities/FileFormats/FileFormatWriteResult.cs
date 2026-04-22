namespace SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

/// <summary>
///     Result payload for file format write operations.
/// </summary>
public sealed class FileFormatWriteResult
{
    /// <summary>
    ///     Gets whether the operation completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    ///     Gets optional failure message when operation fails.
    /// </summary>
    public string? Error { get; init; }
}