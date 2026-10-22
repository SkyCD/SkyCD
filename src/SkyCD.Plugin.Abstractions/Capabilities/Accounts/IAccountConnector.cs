using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Accounts;

/// <summary>Connects an account through the existing plugin runtime.</summary>
public interface IAccountConnector : IPluginCapability
{
    /// <summary>Connects an account, honoring host cancellation.</summary>
    Task<AccountConnectionResult> ConnectAsync(CancellationToken cancellationToken = default);
}
