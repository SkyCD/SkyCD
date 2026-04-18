namespace SkyCD.Plugin.Abstractions.Capabilities.Modal;

/// <summary>
/// Typed payload envelope for modal input/output values.
/// </summary>
public sealed record ModalPayload(
    string TypeId,
    object? Value);
