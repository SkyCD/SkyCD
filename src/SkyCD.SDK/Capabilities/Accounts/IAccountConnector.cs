using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Accounts;

/// <summary>Connects an account through the existing plugin runtime.</summary>
/// <remarks>
/// Single-account-per-provider contract: the host supports exactly one account per provider.
/// <see cref="ConnectAsync"/> connects that default account; its stable <c>AccountId</c> is the
/// per-catalog folder identity and the machine-scoped token key (provider id + account key).
/// Multi-account providers are out of scope until the SDK gains an explicit account-key parameter.
/// </remarks>
public interface IAccountConnector : IPluginCapability
{
    /// <summary>Connects the provider's single (default) account, honoring host cancellation.</summary>
    Task<AccountConnectionResult> ConnectAsync(CancellationToken cancellationToken = default);
}
