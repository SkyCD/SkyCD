namespace SkyCD.Plugin.Abstractions.Capabilities.Modal;

/// <summary>
/// Capability contract for plugins that provide modal dialog contributions.
/// </summary>
public interface IModalPluginCapability : IPluginCapability
{
    /// <summary>
    /// Gets modal descriptor.
    /// </summary>
    ModalDescriptor Modal { get; }

    /// <summary>
    /// Opens a plugin-contributed modal request and returns typed output.
    /// </summary>
    Task<ModalOpenResult> OpenModalAsync(ModalOpenRequest request, CancellationToken cancellationToken = default);
}
