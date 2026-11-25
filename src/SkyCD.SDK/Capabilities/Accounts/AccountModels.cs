#pragma warning disable CS1591

using System;

namespace SkyCD.SDK.Capabilities.Accounts;

public sealed record LicenseRecord
{
    public required string Id { get; init; }
    public required string Provider { get; init; }
    public required string Product { get; init; }
    public int Seats { get; init; } = 1;
    public DateTimeOffset? ExpiresUtc { get; init; }
    public string? Notes { get; init; }

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

public sealed record SubscriptionRecord
{
    public required string Id { get; init; }
    public required string Provider { get; init; }
    public required string Product { get; init; }
    public int Seats { get; init; } = 1;
    public DateTimeOffset? RenewsUtc { get; init; }
    public string? Notes { get; init; }

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
