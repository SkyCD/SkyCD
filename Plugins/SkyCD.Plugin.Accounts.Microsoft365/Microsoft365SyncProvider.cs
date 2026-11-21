using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Accounts.Microsoft365.Models;

namespace SkyCD.Plugin.Accounts.Microsoft365;

public sealed class Microsoft365SyncProvider : IAccountSyncProvider
{
    public string ProviderId => Microsoft365Settings.ProviderId;
    public string DisplayName => "Microsoft 365";

    public IReadOnlyList<LicenseRecord> NormalizeLicenses(IEnumerable<ImportedAccountRow> rows)
    {
        return [];
    }

    public IReadOnlyList<SubscriptionRecord> NormalizeSubscriptions(IEnumerable<ImportedAccountRow> rows)
    {
        return (rows ?? []).Select(row => new SubscriptionRecord
        {
            Id = AccountNormalization.BuildId(ProviderId, "subscription",
                AccountNormalization.ResolveName(row.Product, Microsoft365Products.DisplayNames, "plan")),
            Provider = ProviderId,
            Product = AccountNormalization.ResolveName(row.Product, Microsoft365Products.DisplayNames, "plan"),
            Seats = Math.Max(0, row.Seats),
            RenewsUtc = row.RenewsUtc,
            Notes = string.IsNullOrWhiteSpace(row.Notes) ? null : row.Notes.Trim()
        }).ToList();
    }
}
