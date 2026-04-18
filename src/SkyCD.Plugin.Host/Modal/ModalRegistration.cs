namespace SkyCD.Plugin.Host.Modal;

/// <summary>
/// Host-facing projection of a plugin-provided modal descriptor.
/// </summary>
public sealed record ModalRegistration(
    string PluginId,
    string ModalId,
    string Title,
    int Width,
    int Height,
    IReadOnlyCollection<string> RequiredPermissions,
    bool IsBlocking,
    bool AllowReentry,
    string? InputTypeId,
    bool InputRequired,
    string? OutputTypeId,
    bool OutputRequired);
