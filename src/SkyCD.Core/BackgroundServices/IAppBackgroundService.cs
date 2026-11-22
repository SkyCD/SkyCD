using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Core.BackgroundServices;

/// <summary>
/// An in-process service with an explicit lifecycle that is drained on application shutdown.
/// </summary>
public interface IAppBackgroundService
{
    /// <summary>
    /// Starts the service. Called on a background context, in registration order.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the service and releases its resources. Called in reverse registration order
    /// with a bounded total drain timeout.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken);
}
