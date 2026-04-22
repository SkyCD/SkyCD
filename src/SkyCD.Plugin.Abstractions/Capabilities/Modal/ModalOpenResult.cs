namespace SkyCD.Plugin.Abstractions.Capabilities.Modal;

/// <summary>
///     Result payload for modal operations.
/// </summary>
public sealed class ModalOpenResult
{
    /// <summary>
    ///     Gets whether the modal completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    ///     Gets whether the modal flow was canceled by host timeout/cancellation.
    /// </summary>
    public bool Canceled { get; init; }

    /// <summary>
    ///     Gets optional typed output payload.
    /// </summary>
    public ModalPayload? Output { get; init; }

    /// <summary>
    ///     Gets optional failure message.
    /// </summary>
    public string? Error { get; init; }
}