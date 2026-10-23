# OAuth foundation

SkyCD core provides OAuth 2.0 authorization-code login for public native clients,
with S256 PKCE and an external system browser. Provider endpoints, scopes and
provider-specific options belong to plugins. Core contains no vendor adapters or
client secrets. This is OAuth, not an OpenID Connect identity-validation service.

## Using the host service

A parameterless plugin capability can implement `IOAuthClient` in addition to its
other capability interfaces. The host calls `InitializeOAuth` before registering
the plugin services. Discovery continues to use assembly metadata and capability
types; no manifest file is needed.

```csharp
using SkyCD.Plugin.Abstractions.OAuth;

public sealed class AccountCapability : IOAuthClient
{
    private IOAuthService? oauth;

    public void InitializeOAuth(IOAuthService service) => oauth = service;

    // Call from an explicit user action such as a menu command or ConnectAsync.
    public Task ConnectAsync(string localAccountKey, CancellationToken cancellationToken)
        => oauth!.LoginAsync(provider, localAccountKey, cancellationToken);

    private readonly OAuthProvider provider = new()
    {
        Id = "my-provider",
        ClientId = "registered-public-client-id",
        AuthorizationEndpoint = new Uri("https://provider.example/authorize"),
        TokenEndpoint = new Uri("https://provider.example/token"),
        Scopes = ["read", "offline_access"]
    };
}
```

Register a native/public client with the provider. The callback is
`http://127.0.0.1:{port}/oauth/callback`. By default the host binds an available
ephemeral port before opening the browser. If a provider requires a fixed
registered port, set `RedirectPort`. Endpoints must be absolute HTTPS URLs without
userinfo, query strings or fragments. The listener accepts only the callback path
on IPv4 loopback, limits request sizes and times, and closes after completion,
cancellation or the five-minute login timeout. State, callback origin/path and
duplicate response parameters are checked before exchanging the code.

`AuthorizationParameters` supports provider options such as consent prompts or
offline-access options. It cannot override state, PKCE, client ID, scopes,
redirect URI or response type, and cannot contain a client secret. Use HTTPS
endpoints directly: the default token HTTP client does not follow redirects.

The account key is a **local slot selected by the plugin**, not a verified remote
identity. Use different keys for personal/work accounts. If remote identity is
needed, resolve it through the provider's authenticated API in the plugin. Failed
or canceled login does not overwrite an existing slot.

Call `GetAccessTokenAsync(provider, accountKey)` immediately before an API request.
It reuses a valid token and refreshes within 60 seconds of expiry. A provider that
does not issue refresh tokens can be used until its access token expires. Refresh
rotation is persisted; omission of a new refresh token preserves the previous one.
Concurrent login, refresh and logout for the same slot are serialized within the
host process. No background timer, browser login on startup, or automatic retry of
failed API requests is performed. Providers must return a nonempty bearer token
and a positive numeric `expires_in` in seconds.

`GetAccountsAsync` lists saved local slots. `SignOutAsync` removes a slot locally;
remote logout/revocation is provider-specific and belongs to the plugin.
`OAuthException.Failure == OAuthFailure.LoginRequired` means the plugin should ask
the user to connect again. A refresh rejection with `invalid_grant` removes the
unusable session. Transient token endpoint failures retain it for a later retry.
Cancellation propagates as `OperationCanceledException`. Provider response bodies
and callback error descriptions are never included in OAuth exceptions.

## Storage and host extension points

The default Windows host writes atomically replaced DPAPI-encrypted files under
`%LOCALAPPDATA%/SkyCD/OAuth`. DPAPI uses the current OS user's protection; there is
no plaintext token cache or application-managed encryption key. Credentials do not
enter catalog documents or synchronized catalog storage. Files contain encrypted
account IDs, access tokens, refresh tokens and expiry. Namespace and file names
are hashed, and namespaces bind the plugin assembly identity, provider ID,
client ID, endpoints and scopes. Changing those settings intentionally creates a
separate session namespace.

The default protector is Windows-only. Other platforms fail with
`SecureStorageUnavailable` rather than saving credentials insecurely. A host port
must register an OS-backed `IOAuthDataProtector` or `IOAuthTokenStore` in the root
container **before resolving `OAuthService`**. `IOAuthBrowser` is also replaceable.
The core interfaces allow keychain/secret-service integrations without changing
plugin contracts. The default store requires an interactive OS user profile.

These services coordinate one app process; concurrent processes sharing the same
token directory are not supported for refresh-token rotation. Plugin scoping
prevents accidental account collisions, not malicious in-process access: plugins
are trusted code. Never log or place returned tokens in command arguments,
notifications, URLs, catalog documents or error messages.

## Validation

`OAuthTests` in `SkyCD.Plugin.Runtime.Tests` covers host injection, PKCE/form
encoding, state/redirect rejection, authorization denial, real loopback callbacks
and listener cleanup, cancellation, refresh rotation and concurrency, account and
plugin isolation, configuration changes, malformed responses, and encrypted
Windows persistence/reload/tamper detection. Other platforms verify that the
Windows protector fails closed. Browser launching is replaced by an automated
callback; no live provider account or credentials are needed.
