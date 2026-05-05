using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Menu;

/// <summary>
/// Public host API exposed to plugin menu commands.
/// </summary>
public interface IHostCommandApi
{
    /// <summary>
    /// Requests navigation to the specified node.
    /// </summary>
    Task NavigateToNodeAsync(long nodeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a non-blocking host notification.
    /// </summary>
    Task NotifyAsync(string message, CancellationToken cancellationToken = default);
}
