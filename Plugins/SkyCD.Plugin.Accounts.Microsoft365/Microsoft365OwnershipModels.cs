using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Accounts.Microsoft365;

/// <summary>A Microsoft 365 subscription owned by the connected account.</summary>
public sealed class Microsoft365Subscription
{
    public string SubscriptionId { get; set; } = string.Empty;

    public string SkuPartNumber { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int Seats { get; set; }

    public DateTimeOffset? RenewsUtc { get; set; }

    public bool IsTrial { get; set; }

    /// <summary>Gets or sets the provider-reported lifecycle status.</summary>
    public string ProviderStatus { get; set; } = string.Empty;

    public string? Notes { get; set; }

    /// <summary>Derives the display status without storing it.</summary>
    public string EvaluateStatus(DateTimeOffset asOf)
    {
        var status = ProviderStatus.Trim();
        if (status.Equals("suspended", StringComparison.OrdinalIgnoreCase)
            || status.Equals("deleted", StringComparison.OrdinalIgnoreCase)
            || status.Equals("lockedOut", StringComparison.OrdinalIgnoreCase)
            || status.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return "suspended";
        }

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

/// <summary>A Click-to-Run Office package found on this machine.</summary>
public sealed class Microsoft365OfficePackage
{
    public string ReleaseId { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? Platform { get; set; }
}

/// <summary>A classic Office application executable found on this machine.</summary>
public sealed class OfficeAppInstall
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? Suite { get; set; }
}

/// <summary>An owned subscription linked to the locally installed Office applications.</summary>
public sealed class Microsoft365ProductOwnership
{
    public string SubscriptionId { get; set; } = string.Empty;

    public string SkuPartNumber { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public int Seats { get; set; }

    public DateTimeOffset? RenewsUtc { get; set; }

    public bool IsTrial { get; set; }

    public string Status { get; set; } = "active";

    public bool Installed { get; set; }

    public List<OfficeAppInstall> InstalledApps { get; set; } = [];
}

/// <summary>Persisted Microsoft 365 ownership snapshot. Contains no tokens or credentials.</summary>
public sealed class Microsoft365OwnershipSnapshot
{
    public int Version { get; set; } = 1;

    public string AccountKey { get; set; } = Microsoft365Settings.DefaultAccountKey;

    public bool Connected { get; set; }

    public DateTimeOffset? LastSyncedUtc { get; set; }

    public List<Microsoft365Subscription> Subscriptions { get; set; } = [];

    public List<OfficeAppInstall> InstalledOfficeApps { get; set; } = [];
}
