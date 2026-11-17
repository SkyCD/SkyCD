using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Accounts;

/// <summary>
/// Normalizes Microsoft 365 account records. Microsoft products are commonly referenced by SKU
/// part numbers; known SKUs map to display names and unknown values pass through unchanged.
/// Microsoft 365 records are managed as subscriptions.
/// </summary>
public sealed class Microsoft365SyncProvider : IAccountSyncProvider
{
    public string ProviderId => "microsoft365";
    public string DisplayName => "Microsoft 365";

    private static readonly Dictionary<string, string> SkuPartNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["O365_BUSINESS_ESSENTIALS"] = "Microsoft 365 Business Basic",
        ["O365_BUSINESS_PREMIUM"] = "Microsoft 365 Business Standard",
        ["O365_BUSINESS"] = "Microsoft 365 Apps for business",
        ["SPE_E3"] = "Microsoft 365 E3",
        ["SPE_E5"] = "Microsoft 365 E5",
        ["ENTERPRISEPACK"] = "Office 365 E3",
        ["ENTERPRISEPREMIUM"] = "Office 365 E5",
        ["EXCHANGESTANDARD"] = "Exchange Online (Plan 1)",
        ["EXCHANGEENTERPRISE"] = "Exchange Online (Plan 2)",
        ["FLOW_FREE"] = "Power Automate Free",
        ["POWER_BI_STANDARD"] = "Power BI",
        ["TEAMS_EXPLORATORY"] = "Teams Exploratory"
    };

    public IReadOnlyList<LicenseRecord> NormalizeLicenses(IEnumerable<ImportedAccountRow> rows)
    {
        return [];
    }

    public IReadOnlyList<SubscriptionRecord> NormalizeSubscriptions(IEnumerable<ImportedAccountRow> rows)
    {
        return (rows ?? []).Select(row => new SubscriptionRecord
        {
            Id = AccountNormalization.BuildId(ProviderId, "subscription",
                AccountNormalization.ResolveName(row.Product, SkuPartNumbers, "plan")),
            Provider = ProviderId,
            Product = AccountNormalization.ResolveName(row.Product, SkuPartNumbers, "plan"),
            Seats = Math.Max(0, row.Seats),
            RenewsUtc = row.RenewsUtc,
            Notes = string.IsNullOrWhiteSpace(row.Notes) ? null : row.Notes.Trim()
        }).ToList();
    }
}
