using System;

namespace SkyCD.Plugin.Abstractions.OAuth;

/// <summary>A sanitized OAuth failure; provider response bodies and credentials are never included.</summary>
public sealed class OAuthException : Exception
{
    /// <summary>Initializes a failure with a stable host-defined reason code.</summary>
    public OAuthException(OAuthFailure failure) : base($"OAuth operation failed ({failure}).")
    {
        Failure = failure;
    }

    /// <summary>Gets the reason; a missing or expired session requires a new login.</summary>
    public OAuthFailure Failure { get; }
}
