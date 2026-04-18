namespace SkyCD.Plugin.Abstractions.Capabilities.Modal;

/// <summary>
/// Request payload for opening a contributed modal.
/// </summary>
public sealed class ModalOpenRequest
{
    /// <summary>
    /// Gets modal identifier.
    /// </summary>
    public required string ModalId { get; init; }

    /// <summary>
    /// Gets optional input payload.
    /// </summary>
    public object? Input { get; init; }
}
