namespace SkyCD.SDK.Capabilities.Modal;

/// <summary>
/// Describes allowed payload type for modal input/output.
/// </summary>
public sealed record ModalPayloadContract(
    string TypeId,
    bool IsRequired = false);