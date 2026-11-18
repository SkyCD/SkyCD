using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AsyncKeyedLock;
using Duende.IdentityModel;
using Duende.IdentityModel.Client;
using SkyCD.Plugin.Abstractions.OAuth;

namespace SkyCD.Core.OAuth;

/// <summary>Shared coordinator; scoped clients never accept a caller-supplied plugin identity.</summary>
public sealed class OAuthService(HttpClient httpClient, IOAuthTokenStore tokenStore, IOAuthBrowser browser, TimeProvider clock)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IOAuthTokenStore _tokenStore = tokenStore;
    private readonly IOAuthBrowser _browser = browser;
    private readonly TimeProvider _clock = clock;
    // Entries are reference-counted and removed once no caller holds or awaits them.
    private readonly AsyncKeyedLocker<string> _locks = new(StringComparer.Ordinal);

    internal int LockCount => _locks.Index.Count;

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
            using var gate = await owner.LockAsync(key, accountId, cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            var verifier = CryptoRandom.CreateUniqueId();
            var state = CryptoRandom.CreateUniqueId();
            Uri? redirectUri = null;
            var callback = await owner._browser.AuthorizeAsync(redirect =>
            {
                redirectUri = redirect;
                var url = new RequestUrl(provider.AuthorizationEndpoint.AbsoluteUri).CreateAuthorizeUrl(
                    provider.ClientId,
                    responseType: "code",
                    scope: string.Join(' ', provider.Scopes),
                    redirectUri: redirect.AbsoluteUri,
                    state: state,
                    codeChallenge: CodeChallenge(verifier),
                    codeChallengeMethod: "S256",
                    extra: new Parameters(provider.AuthorizationParameters));
                return new Uri(url);
            }, provider.RedirectPort, timeout.Token);

            if (redirectUri is null || callback.GetLeftPart(UriPartial.Path) != redirectUri.GetLeftPart(UriPartial.Path)
                || !string.IsNullOrEmpty(callback.Fragment))
                throw new OAuthException(OAuthFailure.InvalidCallback);
            AuthorizeResponse authorization;
            try
            {
                authorization = new AuthorizeResponse(callback.AbsoluteUri);
            }
            catch (ArgumentException)
            {
                throw new OAuthException(OAuthFailure.InvalidCallback);
            }
            catch (InvalidOperationException)
            {
                throw new OAuthException(OAuthFailure.InvalidCallback);
            }
            if (authorization.State != state)
                throw new OAuthException(OAuthFailure.InvalidCallback);
            if (authorization.IsError)
                throw new OAuthException(OAuthFailure.AuthorizationDenied);
            var code = authorization.Code;
            if (string.IsNullOrWhiteSpace(code))
                throw new OAuthException(OAuthFailure.InvalidCallback);

            using var tokenResponse = await owner._httpClient.RequestAuthorizationCodeTokenAsync(new AuthorizationCodeTokenRequest
            {
                Address = provider.TokenEndpoint.AbsoluteUri,
                ClientId = provider.ClientId,
                ClientCredentialStyle = ClientCredentialStyle.PostBody,
                Code = code,
                RedirectUri = redirectUri.AbsoluteUri,
                CodeVerifier = verifier
            }, timeout.Token);
            var session = owner.ToSession(tokenResponse, accountId, null);
            await owner._tokenStore.WriteAsync(key, session, timeout.Token);
        }

        public async Task<string> GetAccessTokenAsync(OAuthProvider provider, string accountId, CancellationToken cancellationToken = default)
        {
            var key = Namespace(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
            using var gate = await owner.LockAsync(key, accountId, cancellationToken);
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
                using var tokenResponse = await owner._httpClient.RequestRefreshTokenAsync(new RefreshTokenRequest
                {
                    Address = provider.TokenEndpoint.AbsoluteUri,
                    ClientId = provider.ClientId,
                    ClientCredentialStyle = ClientCredentialStyle.PostBody,
                    RefreshToken = session.RefreshToken
                }, cancellationToken);
                session = owner.ToSession(tokenResponse, accountId, session.RefreshToken);
            }
            catch (OAuthException exception) when (exception.Failure == OAuthFailure.LoginRequired)
            {
                await owner._tokenStore.DeleteAsync(key, accountId, cancellationToken);
                throw;
            }
            await owner._tokenStore.WriteAsync(key, session, cancellationToken);
            return session.AccessToken;
        }

        public Task<IReadOnlyList<string>> GetAccountsAsync(OAuthProvider provider, CancellationToken cancellationToken = default)
            => owner._tokenStore.GetAccountsAsync(Namespace(provider), cancellationToken);

        public async Task SignOutAsync(OAuthProvider provider, string accountId, CancellationToken cancellationToken = default)
        {
            var key = Namespace(provider);
            ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
            using var gate = await owner.LockAsync(key, accountId, cancellationToken);
            await owner._tokenStore.DeleteAsync(key, accountId, cancellationToken);
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

    private ValueTask<IDisposable> LockAsync(string namespaceId, string accountId, CancellationToken cancellationToken)
        => _locks.LockAsync(namespaceId + ":" + accountId, cancellationToken);

    // Never propagate raw provider bodies, which can echo tokens or authorization codes.
    private OAuthSession ToSession(TokenResponse response, string accountId, string? previousRefreshToken)
    {
        if (response.Exception is JsonException)
            throw new OAuthException(OAuthFailure.InvalidTokenResponse);
        if (response.Exception is not null)
            ExceptionDispatchInfo.Capture(response.Exception).Throw();
        if (response.Json is not { ValueKind: JsonValueKind.Object } json)
            throw new OAuthException(OAuthFailure.InvalidTokenResponse);
        if (response.HttpResponse is { IsSuccessStatusCode: false })
        {
            if (previousRefreshToken is not null && json.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String && error.GetString() == "invalid_grant")
                throw new OAuthException(OAuthFailure.LoginRequired);
            throw new OAuthException(OAuthFailure.TokenRequestFailed);
        }
        if (!json.TryGetProperty("access_token", out var access) || access.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(access.GetString())
            || !json.TryGetProperty("token_type", out var type) || type.ValueKind != JsonValueKind.String
            || !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase)
            || !json.TryGetProperty("expires_in", out var expiry) || expiry.ValueKind != JsonValueKind.Number || !expiry.TryGetInt64(out var seconds)
            || seconds <= 0 || seconds > 315360000)
            throw new OAuthException(OAuthFailure.InvalidTokenResponse);
        string? refreshToken = previousRefreshToken;
        if (json.TryGetProperty("refresh_token", out var refresh))
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

    private static bool IsEndpoint(Uri? uri) => uri is { IsAbsoluteUri: true, Scheme: "https", Query.Length: 0, Fragment.Length: 0, UserInfo.Length: 0 };

    private static string CodeChallenge(string verifier)
        => Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
