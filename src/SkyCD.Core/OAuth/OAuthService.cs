using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.OAuth;

namespace SkyCD.Core.OAuth;

/// <summary>Shared coordinator; scoped clients never accept a caller-supplied plugin identity.</summary>
public sealed class OAuthService(HttpClient httpClient, IOAuthTokenStore tokenStore, IOAuthBrowser browser, TimeProvider clock)
{
    private readonly IOAuthTokenStore _tokenStore = tokenStore;
    private readonly IOAuthBrowser _browser = browser;
    private readonly TimeProvider _clock = clock;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public IOAuthService ForPlugin(string pluginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        return new Client(this, pluginId);
    }

    private sealed class Client(OAuthService owner, string pluginId) : IOAuthService
    {
        public async Task LoginAsync(OAuthProvider provider, string accountId, CancellationToken cancellationToken = default)
        {
            var key = Namespace(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
            var gate = owner._locks.GetOrAdd(key + ":" + accountId, static _ => new SemaphoreSlim(1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
                var state = Base64Url(RandomNumberGenerator.GetBytes(32));
                Uri? redirectUri = null;
                var callback = await owner._browser.AuthorizeAsync(redirect =>
                {
                    redirectUri = redirect;
                    var parameters = new Dictionary<string, string>
                    {
                        ["response_type"] = "code",
                        ["client_id"] = provider.ClientId,
                        ["redirect_uri"] = redirect.AbsoluteUri,
                        ["scope"] = string.Join(' ', provider.Scopes),
                        ["state"] = state,
                        ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                        ["code_challenge_method"] = "S256"
                    };
                    foreach (var parameter in provider.AuthorizationParameters)
                        parameters.Add(parameter.Key, parameter.Value);
                    return new Uri(provider.AuthorizationEndpoint.AbsoluteUri + "?" + string.Join('&',
                        parameters.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
                }, provider.RedirectPort, timeout.Token);

                if (redirectUri is null || callback.GetLeftPart(UriPartial.Path) != redirectUri.GetLeftPart(UriPartial.Path)
                    || !string.IsNullOrEmpty(callback.Fragment))
                    throw new OAuthException(OAuthFailure.InvalidCallback);
                var values = ParseQuery(callback.Query);
                if (!values.TryGetValue("state", out var actualState) || actualState != state)
                    throw new OAuthException(OAuthFailure.InvalidCallback);
                if (values.ContainsKey("error"))
                    throw new OAuthException(OAuthFailure.AuthorizationDenied);
                if (!values.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
                    throw new OAuthException(OAuthFailure.InvalidCallback);

                var session = await owner.RequestTokensAsync(provider, accountId, new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = provider.ClientId,
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri.AbsoluteUri,
                    ["code_verifier"] = verifier
                }, null, timeout.Token);
                await owner._tokenStore.WriteAsync(key, session, timeout.Token);
            }
            finally { gate.Release(); }
        }

        public async Task<string> GetAccessTokenAsync(OAuthProvider provider, string accountId, CancellationToken cancellationToken = default)
        {
            var key = Namespace(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
            var gate = owner._locks.GetOrAdd(key + ":" + accountId, static _ => new SemaphoreSlim(1));
            await gate.WaitAsync(cancellationToken);
            try
            {
                var session = await owner._tokenStore.ReadAsync(key, accountId, cancellationToken)
                    ?? throw new OAuthException(OAuthFailure.LoginRequired);
                if (session.ExpiresAt > owner._clock.GetUtcNow().AddSeconds(60))
                    return session.AccessToken;
                if (string.IsNullOrEmpty(session.RefreshToken) && session.ExpiresAt > owner._clock.GetUtcNow())
                    return session.AccessToken;
                if (string.IsNullOrEmpty(session.RefreshToken))
                    throw new OAuthException(OAuthFailure.LoginRequired);
                try
                {
                    session = await owner.RequestTokensAsync(provider, accountId, new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["client_id"] = provider.ClientId,
                        ["refresh_token"] = session.RefreshToken
                    }, session.RefreshToken, cancellationToken);
                }
                catch (OAuthException exception) when (exception.Failure == OAuthFailure.LoginRequired)
                {
                    await owner._tokenStore.DeleteAsync(key, accountId, cancellationToken);
                    throw;
                }
                await owner._tokenStore.WriteAsync(key, session, cancellationToken);
                return session.AccessToken;
            }
            finally { gate.Release(); }
        }

        public Task<IReadOnlyList<string>> GetAccountsAsync(OAuthProvider provider, CancellationToken cancellationToken = default)
            => owner._tokenStore.GetAccountsAsync(Namespace(provider), cancellationToken);

        public async Task SignOutAsync(OAuthProvider provider, string accountId, CancellationToken cancellationToken = default)
        {
            var key = Namespace(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
            var gate = owner._locks.GetOrAdd(key + ":" + accountId, static _ => new SemaphoreSlim(1));
            await gate.WaitAsync(cancellationToken);
            try { await owner._tokenStore.DeleteAsync(key, accountId, cancellationToken); }
            finally { gate.Release(); }
        }

        private string Namespace(OAuthProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (string.IsNullOrWhiteSpace(provider.Id) || string.IsNullOrWhiteSpace(provider.ClientId)
                || !IsEndpoint(provider.AuthorizationEndpoint) || !IsEndpoint(provider.TokenEndpoint)
                || provider.RedirectPort is < 0 or > 65535 || provider.Scopes is null
                || provider.Scopes.Any(string.IsNullOrWhiteSpace) || provider.AuthorizationParameters is null
                || provider.AuthorizationParameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null
                    || pair.Key is "response_type" or "client_id" or "redirect_uri" or "scope" or "state"
                        or "code_challenge" or "code_challenge_method" or "client_secret"))
                throw new OAuthException(OAuthFailure.InvalidConfiguration);
            // Configuration binding prevents refreshing a saved grant against a changed endpoint/client.
            return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                pluginId,
                provider.Id,
                provider.ClientId,
                AuthorizationEndpoint = provider.AuthorizationEndpoint.AbsoluteUri,
                TokenEndpoint = provider.TokenEndpoint.AbsoluteUri,
                Scopes = provider.Scopes.Order(StringComparer.Ordinal).ToArray()
            })));
        }
    }

    private async Task<OAuthSession> RequestTokensAsync(OAuthProvider provider, string accountId,
        Dictionary<string, string> fields, string? previousRefreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, provider.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        using var response = await httpClient.SendAsync(request, cancellationToken);
        // Never propagate raw provider bodies, which can echo tokens or authorization codes.
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new OAuthException(OAuthFailure.InvalidTokenResponse);
            if (!response.IsSuccessStatusCode)
            {
                if (previousRefreshToken is not null && root.TryGetProperty("error", out var error)
                    && error.ValueKind == JsonValueKind.String && error.GetString() == "invalid_grant")
                    throw new OAuthException(OAuthFailure.LoginRequired);
                throw new OAuthException(OAuthFailure.TokenRequestFailed);
            }
            if (!root.TryGetProperty("access_token", out var access) || access.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(access.GetString())
                || !root.TryGetProperty("token_type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)
                || !root.TryGetProperty("expires_in", out var expiry) || expiry.ValueKind != JsonValueKind.Number || !expiry.TryGetInt64(out var seconds)
                || seconds <= 0 || seconds > 315360000)
                throw new OAuthException(OAuthFailure.InvalidTokenResponse);
            string? refreshToken = previousRefreshToken;
            if (root.TryGetProperty("refresh_token", out var refresh))
            {
                if (refresh.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(refresh.GetString()))
                    throw new OAuthException(OAuthFailure.InvalidTokenResponse);
                refreshToken = refresh.GetString();
            }
            return new OAuthSession
            {
                AccountId = accountId,
                AccessToken = access.GetString()!,
                RefreshToken = refreshToken,
                ExpiresAt = _clock.GetUtcNow().AddSeconds(seconds)
            };
        }
        catch (JsonException) { throw new OAuthException(OAuthFailure.InvalidTokenResponse); }
    }

    private static bool IsEndpoint(Uri? uri) => uri is { IsAbsoluteUri: true, Scheme: "https", Query.Length: 0, Fragment.Length: 0, UserInfo.Length: 0 };

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var name = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
            if (!result.TryAdd(name, value))
                throw new OAuthException(OAuthFailure.InvalidCallback);
        }
        return result;
    }
}
