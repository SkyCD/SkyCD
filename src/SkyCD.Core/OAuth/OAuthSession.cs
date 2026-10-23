using System;

namespace SkyCD.Core.OAuth;

/// <summary>Persisted credentials. Deliberately has no generated value-printing ToString.</summary>
public sealed class OAuthSession
{
    public required string AccountId { get; init; }
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}
