using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Ownership;

namespace SkyCD.SDK.Capabilities.Accounts;

/// <summary>
/// Host-facing account surface: sign-in, owned-product snapshot, manual sync and sign-out.
/// The host reads the token-free ownership snapshot through <see cref="GetStatusAsync"/> and never
/// reaches the provider's token store.
/// </summary>
/// <remarks>
/// Single-account-per-provider: every method acts on the provider's default account. The account
/// folder in the open catalog is the catalog's record of the connection (provider id, account key,
/// display metadata); the token stays in the host's encrypted OAuth store scoped by provider +
/// account key and is never written to the catalog.
/// </remarks>
public interface IAccountSession : IAccountConnector, IOwnershipProvider
{
    /// <summary>Returns the current connection state and the owned products from the last snapshot.</summary>
    Task<AccountSessionStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the stored tokens for the connector's default account; catalog folders stay as disconnected records.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}
