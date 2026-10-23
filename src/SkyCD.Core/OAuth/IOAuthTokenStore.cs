using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Core.OAuth;

/// <summary>Host-replaceable secure persistence. Namespace keys include plugin and provider configuration.</summary>
public interface IOAuthTokenStore
{
    Task<OAuthSession?> ReadAsync(string namespaceId, string accountId, CancellationToken cancellationToken);
    Task WriteAsync(string namespaceId, OAuthSession session, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetAccountsAsync(string namespaceId, CancellationToken cancellationToken);
    Task DeleteAsync(string namespaceId, string accountId, CancellationToken cancellationToken);
}
