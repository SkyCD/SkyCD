namespace SkyCD.Plugin.Abstractions.Capabilities.Modal;

/// <summary>
/// Result payload for modal operations.
/// </summary>
public sealed class ModalOpenResult
{
    /// <summary>
    /// Gets whether the modal completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets optional output payload.
    /// </summary>
    public object? Output { get; init; }

    /// <summary>
    /// Gets optional failure message.
    /// </summary>
    public string? Error { get; init; }
}
