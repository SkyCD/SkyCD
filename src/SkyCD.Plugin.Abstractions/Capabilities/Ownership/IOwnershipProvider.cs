using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Ownership;

/// <summary>Synchronizes ownership information from a plugin's store or library.</summary>
public interface IOwnershipProvider : IPluginCapability
{
    /// <summary>Synchronizes ownership, honoring host cancellation.</summary>
    Task SyncAsync(CancellationToken cancellationToken = default);
}
