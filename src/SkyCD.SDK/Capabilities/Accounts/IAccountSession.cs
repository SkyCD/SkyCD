using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Ownership;

namespace SkyCD.SDK.Capabilities.Accounts;

/// <summary>
/// Host-facing account surface: sign-in, owned-product snapshot, manual sync and sign-out.
/// The host reads the token-free ownership snapshot through <see cref="GetStatusAsync"/> and never
/// reaches the provider's token store.
/// </summary>
public interface IAccountSession : IAccountConnector, IOwnershipProvider
{
    /// <summary>Returns the current connection state and the owned products from the last snapshot.</summary>
    Task<AccountSessionStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the stored tokens for the connector's default account.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
