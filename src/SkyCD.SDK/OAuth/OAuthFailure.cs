namespace SkyCD.Plugin.Abstractions.OAuth;

/// <summary>Stable failure reasons safe to display without leaking provider responses.</summary>
public enum OAuthFailure
{
    /// <summary>Provider configuration is invalid.</summary>
    InvalidConfiguration,
    /// <summary>The browser response was invalid or did not match the login attempt.</summary>
    InvalidCallback,
    /// <summary>The user or provider denied authorization.</summary>
    AuthorizationDenied,
    /// <summary>The token endpoint rejected the request.</summary>
    TokenRequestFailed,
    /// <summary>The endpoint returned an unusable token response.</summary>
    InvalidTokenResponse,
    /// <summary>No usable session exists. Log in again.</summary>
    LoginRequired,
    /// <summary>The operating system secure store is unavailable.</summary>
    SecureStorageUnavailable
}
