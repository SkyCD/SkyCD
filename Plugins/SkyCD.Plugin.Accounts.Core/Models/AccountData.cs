using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Accounts;

namespace SkyCD.Plugin.Accounts.Core.Models;

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
