using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.OAuth;

/// <summary>Vendor-neutral configuration for a public native OAuth client (no client secret).</summary>
public sealed class OAuthProvider
{
    /// <summary>Gets the plugin's stable provider identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the registered public client identifier.</summary>
    public required string ClientId { get; init; }
    /// <summary>Gets the HTTPS authorization endpoint.</summary>
    public required Uri AuthorizationEndpoint { get; init; }
    /// <summary>Gets the HTTPS token endpoint.</summary>
    public required Uri TokenEndpoint { get; init; }
    /// <summary>Gets the requested scopes.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];
    /// <summary>Gets provider-specific authorization options. Standard protocol fields cannot be overridden.</summary>
    public IReadOnlyDictionary<string, string> AuthorizationParameters { get; init; } = new Dictionary<string, string>();
    /// <summary>Gets the loopback callback port; zero requests an ephemeral port.</summary>
    public int RedirectPort { get; init; }
}
