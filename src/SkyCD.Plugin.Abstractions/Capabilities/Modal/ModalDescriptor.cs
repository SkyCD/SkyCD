namespace SkyCD.Plugin.Abstractions.Capabilities.Modal;

/// <summary>
/// Describes a modal dialog contributed by a plugin.
/// </summary>
public sealed record ModalDescriptor(
    string ModalId,
    string Title,
    int Width,
    int Height,
    IReadOnlyCollection<string>? RequiredPermissions = null,
    ModalPayloadContract? InputContract = null,
    ModalPayloadContract? OutputContract = null,
    bool IsBlocking = true,
    bool AllowReentry = false);
