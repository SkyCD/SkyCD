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
    /// Gets optional typed input payload.
    /// </summary>
    public ModalPayload? Input { get; init; }

    /// <summary>
    /// Gets permissions granted by host for this open operation.
    /// </summary>
    public IReadOnlyCollection<string> GrantedPermissions { get; init; } = [];
}
