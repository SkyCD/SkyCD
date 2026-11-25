#pragma warning disable CS1591

using System;
using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.Accounts;

public sealed record ImportedAccountRow
{
    public string? Product { get; init; }
    public int Seats { get; init; } = 1;
    public DateTimeOffset? ExpiresUtc { get; init; }
    public DateTimeOffset? RenewsUtc { get; init; }
    public string? Notes { get; init; }
}

public interface IAccountSyncProvider : IPluginCapability
{
    string ProviderId { get; }
    string DisplayName { get; }
    IReadOnlyList<LicenseRecord> NormalizeLicenses(IEnumerable<ImportedAccountRow> rows);
    IReadOnlyList<SubscriptionRecord> NormalizeSubscriptions(IEnumerable<ImportedAccountRow> rows);
}
