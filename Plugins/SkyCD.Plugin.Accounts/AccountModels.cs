using System;

namespace SkyCD.Plugin.Accounts;

/// <summary>
/// A software license record (perpetual or term).
/// </summary>
public sealed record LicenseRecord
{
    public required string Id { get; init; }
    public required string Provider { get; init; }
    public required string Product { get; init; }
    public int Seats { get; init; } = 1;
    public DateTimeOffset? ExpiresUtc { get; init; }
    public string? Notes { get; init; }

    /// <summary>
    /// Derives the license status at the given moment without mutating the record.
    /// </summary>
    public string EvaluateStatus(DateTimeOffset asOf)
    {
        if (ExpiresUtc is null)
        {
            return "no-expiry";
        }

        if (asOf > ExpiresUtc.Value)
        {
            return "expired";
        }

        return (ExpiresUtc.Value - asOf) <= TimeSpan.FromDays(30) ? "expiring" : "active";
    }
}

/// <summary>
/// A subscription record (recurring service entitlement).
/// </summary>
public sealed record SubscriptionRecord
{
    public required string Id { get; init; }
    public required string Provider { get; init; }
    public required string Product { get; init; }
    public int Seats { get; init; } = 1;
    public DateTimeOffset? RenewsUtc { get; init; }
    public string? Notes { get; init; }

    /// <summary>
    /// Derives the subscription status at the given moment without mutating the record.
    /// </summary>
    public string EvaluateStatus(DateTimeOffset asOf)
    {
        if (RenewsUtc is null)
        {
            return "no-renewal";
        }

        if (asOf > RenewsUtc.Value)
        {
            return "expired";
        }

        return (RenewsUtc.Value - asOf) <= TimeSpan.FromDays(30) ? "expiring" : "active";
    }
}
