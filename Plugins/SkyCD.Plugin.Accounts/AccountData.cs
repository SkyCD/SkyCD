using System.Collections.Generic;

namespace SkyCD.Plugin.Accounts;

/// <summary>
/// Persisted ownership/account data.
/// </summary>
public sealed class AccountData
{
    public int Version { get; set; } = 1;

    public List<LicenseRecord> Licenses { get; set; } = [];

    public List<SubscriptionRecord> Subscriptions { get; set; } = [];

    public Dictionary<string, string> LastSyncedUtcByProvider { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
}
