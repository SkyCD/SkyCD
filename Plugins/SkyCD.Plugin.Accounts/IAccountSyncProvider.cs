using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Accounts;

/// <summary>
/// A raw ownership record awaiting provider normalization (for example from <c>--from</c> files).
/// </summary>
public sealed record ImportedAccountRow
{
    public string? Product { get; init; }
    public int Seats { get; init; } = 1;
    public DateTimeOffset? ExpiresUtc { get; init; }
    public DateTimeOffset? RenewsUtc { get; init; }
    public string? Notes { get; init; }
}

/// <summary>
/// Normalizes provider records into canonical store records with deterministic IDs.
/// </summary>
public interface IAccountSyncProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    IReadOnlyList<LicenseRecord> NormalizeLicenses(IEnumerable<ImportedAccountRow> rows);
    IReadOnlyList<SubscriptionRecord> NormalizeSubscriptions(IEnumerable<ImportedAccountRow> rows);
}
