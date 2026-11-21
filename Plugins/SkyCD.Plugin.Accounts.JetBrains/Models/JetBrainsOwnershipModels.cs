using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Accounts.JetBrains.Models;

/// <summary>A JetBrains license or subscription owned by the connected account.</summary>
public sealed class JetBrainsLicense
{
    public string LicenseId { get; set; } = string.Empty;

    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string LicenseType { get; set; } = "license";

    public DateTimeOffset? ExpiresUtc { get; set; }

    public bool RenewsAutomatically { get; set; }

    public bool Suspended { get; set; }

    public bool IsTrial { get; set; }

    public int Seats { get; set; } = 1;

    public string? AssigneeEmail { get; set; }

    /// <summary>Derives the display status without storing it.</summary>
    public string EvaluateStatus(DateTimeOffset asOf)
    {
        if (Suspended)
        {
            return "suspended";
        }

        if (ExpiresUtc is null)
        {
            return "active";
        }

        if (asOf > ExpiresUtc.Value)
        {
            return "expired";
        }

        return (ExpiresUtc.Value - asOf) <= TimeSpan.FromDays(30) ? "expiring" : "active";
    }
}

/// <summary>A JetBrains IDE executable found on this machine.</summary>
public sealed class JetBrainsIdeInstall
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public string? Version { get; set; }
}

/// <summary>An owned product linked to its local installation state.</summary>
public sealed class JetBrainsProductOwnership
{
    public string ProductCode { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public string LicenseId { get; set; } = string.Empty;

    public string LicenseType { get; set; } = "license";

    public DateTimeOffset? ExpiresUtc { get; set; }

    public bool RenewsAutomatically { get; set; }

    public string Status { get; set; } = "active";

    public int Seats { get; set; } = 1;

    public string? AssigneeEmail { get; set; }

    public bool Installed { get; set; }

    public string? ExecutablePath { get; set; }

    public string? Version { get; set; }
}

/// <summary>Persisted JetBrains ownership snapshot. Contains no tokens or credentials.</summary>
public sealed class JetBrainsOwnershipSnapshot
{
    public int Version { get; set; } = 1;

    public string AccountKey { get; set; } = JetBrainsSettings.DefaultAccountKey;

    public string? AccountEmail { get; set; }

    public bool Connected { get; set; }

    public DateTimeOffset? LastSyncedUtc { get; set; }

    public List<JetBrainsLicense> Licenses { get; set; } = [];

    public List<JetBrainsIdeInstall> InstalledIdes { get; set; } = [];
}
