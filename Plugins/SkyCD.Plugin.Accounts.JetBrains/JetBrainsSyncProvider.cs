using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Accounts.JetBrains.Models;

namespace SkyCD.Plugin.Accounts.JetBrains;

public sealed class JetBrainsSyncProvider : IAccountSyncProvider
{
    public string ProviderId => "jetbrains";
    public string DisplayName => "JetBrains";

    public IReadOnlyList<LicenseRecord> NormalizeLicenses(IEnumerable<ImportedAccountRow> rows)
    {
        return (rows ?? []).Select(row => new LicenseRecord
        {
            Id = AccountNormalization.BuildId(ProviderId, "license",
                AccountNormalization.ResolveName(row.Product, JetBrainsProducts.DisplayNames, "product")),
            Provider = ProviderId,
            Product = AccountNormalization.ResolveName(row.Product, JetBrainsProducts.DisplayNames, "product"),
            Seats = Math.Max(0, row.Seats),
            ExpiresUtc = row.ExpiresUtc,
            Notes = string.IsNullOrWhiteSpace(row.Notes) ? null : row.Notes.Trim()
        }).ToList();
    }

    public IReadOnlyList<SubscriptionRecord> NormalizeSubscriptions(IEnumerable<ImportedAccountRow> rows)
    {
        return [];
    }
}
