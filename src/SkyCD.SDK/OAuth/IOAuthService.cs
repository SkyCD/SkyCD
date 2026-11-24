using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.OAuth;

/// <summary>Plugin-scoped OAuth login and sessions. Never log returned bearer tokens.</summary>
public interface IOAuthService
{
    /// <summary>Opens the system browser and saves a session under a plugin-selected local account key.</summary>
    Task LoginAsync(OAuthProvider provider, string accountId, CancellationToken cancellationToken = default);
    /// <summary>Returns a usable bearer token, refreshing near-expired tokens automatically.</summary>
    Task<string> GetAccessTokenAsync(OAuthProvider provider, string accountId, CancellationToken cancellationToken = default);
    /// <summary>Lists locally connected account keys for this provider configuration.</summary>
    Task<IReadOnlyList<string>> GetAccountsAsync(OAuthProvider provider, CancellationToken cancellationToken = default);
    /// <summary>Removes the local session; provider-side revocation remains the plugin's responsibility.</summary>
    Task SignOutAsync(OAuthProvider provider, string accountId, CancellationToken cancellationToken = default);
}
