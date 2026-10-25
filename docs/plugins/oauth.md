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

The host selects its protector automatically:

| Platform | Protection and requirements |
| --- | --- |
| Windows | Existing current-user DPAPI, with unchanged file format and location. |
| macOS | Native Security.framework `SecItem` generic-password API in the current user's default Keychain. No extra .NET dependency for Keychain. Entries are not synchronized to iCloud. |
| Linux | Secret Service on the current user's D-Bus session, using `Ace4896.DBus.Services.Secrets` 1.4.0 (MIT) and its MIT-licensed Tmds.DBus.Protocol dependency. Requires a running service such as GNOME Keyring or a compatible KWallet implementation, with an unlocked persistent default collection. |

macOS and Linux store one random 256-bit encryption key per hashed OAuth namespace
in the OS credential store. Token files remain under the platform's
`LocalApplicationData/SkyCD/OAuth` directory, encrypted using AES-256-GCM with a
fresh 96-bit nonce, 128-bit authentication tag, version byte, and namespace as
authenticated associated data. Keys are never persisted to files or cached by the
protector. Temporary managed key and plaintext buffers are cleared after use.
Linux also uses the Secret Service DH encrypted transport, without a plaintext
transport fallback. Namespace attributes contain hashes, not account names or tokens.

Missing services, unavailable user profiles, locked collections/keychains, denied
access, malformed keys and corrupted ciphertext fail with sanitized
`SecureStorageUnavailable`. There is no plaintext, session-only keyring, password
file, or generated-on-read fallback. macOS disallows authentication prompts;
Linux checks that the existing default collection is unlocked rather than creating
or unlocking a collection. Unlock the desktop keyring and retry explicitly.
Headless Linux sessions normally cannot persist OAuth sessions without providing
a Secret Service and persistent default collection. OS calls run off the UI
thread; cancellation before publication does not replace a saved token file.

If a keyring key is lost, existing encrypted sessions cannot be recovered. Reads
never regenerate keys. Signing in again can create a new namespace key; other
sessions encrypted with the lost key need to sign in again as well. Sign-out deletes
the encrypted account file even if the keyring is unavailable. Namespace keys stay
in the keyring so signing out one account cannot invalidate the remaining accounts;
they contain no account IDs or tokens and can be removed manually after all sessions
in that namespace are discarded.

Dependency evaluation: libsecret is a viable native API but is LGPL-licensed and
would add a native library prerequisite. The selected managed MIT client speaks
Secret Service directly over D-Bus, avoiding a libsecret package/binary. No LGPL
library is bundled. The operating system's installed keyring service remains
responsible for encrypted storage at rest. Native Keychain avoids third-party
macOS wrappers and uses `SecItem` rather than deprecated `SecKeychain` APIs.
Version 1.4.0 targets .NET 9 and is compatible with .NET 10; it ships generated
bindings without a transitive source-generator dependency. Version 1.6.0 was
evaluated but introduces `Tmds.DBus.Generator` 0.92.0 with missing NuGet license
metadata, which fails this repository's dependency policy. No policy exemption
is needed for the selected dependency graph. `Tmds.DBus.Protocol` is explicitly
pinned to 0.21.3, which includes the security fixes for
[CVE-2026-39959](https://github.com/advisories/GHSA-xrw6-gwf8-vvr9); the client's
older minimum dependency must not be used.

A host can still register an OS-backed `IOAuthDataProtector` or `IOAuthTokenStore`
in the root container **before resolving `OAuthService`**. Unsupported operating
systems fail closed. `IOAuthBrowser` is also replaceable. These defaults require
an interactive OS user profile, and do not weaken storage when the keyring fails.

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

`OAuthStorageTests` exercises platform selection, AES-GCM authentication,
unavailable/invalid key storage, encrypted reload, account and namespace isolation,
cancellation and deletion without requiring a desktop keyring. The `Native OAuth
storage` CI workflow additionally provisions an encrypted Linux keyring within an
isolated D-Bus session and a temporary macOS Keychain, testing real native
persistence/reload/tamper detection and unavailable/locked storage. Native tests are
explicitly skipped locally unless `SKYCD_TEST_NATIVE_OAUTH=1`; the unavailable-store
test requires `SKYCD_TEST_UNAVAILABLE_OAUTH=1` in a deliberately broken/locked test
environment. The existing three-OS CI matrix continues to cover Windows DPAPI.
