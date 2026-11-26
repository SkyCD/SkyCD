using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities;

/// <summary>
/// Optional capability lifecycle the host drives after a plugin is registered. Activation runs once per
/// discovery pass and deactivation runs before the capability instance is dropped, so long-running plugin
/// work keeps one instance per runtime pass and stops when the plugin is disabled.
/// </summary>
public interface IActivatablePluginCapability : IPluginCapability
{
    /// <summary>Starts the capability's runtime work. Called after host services are injected.</summary>
    Task ActivateAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the capability's runtime work and releases its resources.</summary>
    Task DeactivateAsync(CancellationToken cancellationToken = default);
}
