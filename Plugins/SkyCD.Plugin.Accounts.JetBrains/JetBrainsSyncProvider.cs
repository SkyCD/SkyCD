using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;

namespace SkyCD.Plugin.Accounts.JetBrains;

public sealed class JetBrainsSyncProvider : IAccountSyncProvider
{
    public string ProviderId => "jetbrains";
    public string DisplayName => "JetBrains";

    private static readonly Dictionary<string, string> ProductCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["II"] = "IntelliJ IDEA Ultimate",
        ["IIC"] = "IntelliJ IDEA Community",
        ["PS"] = "PhpStorm",
        ["PC"] = "PyCharm",
        ["WS"] = "WebStorm",
        ["GO"] = "GoLand",
        ["RM"] = "RubyMine",
        ["CL"] = "CLion",
        ["DG"] = "DataGrip",
        ["DS"] = "DataSpell",
        ["RD"] = "Rider",
        ["RS"] = "ReSharper",
        ["RSC"] = "ReSharper C++",
        ["DC"] = "dotCover",
        ["DM"] = "dotMemory",
        ["DP"] = "dotPeek",
        ["TC"] = "TeamCity",
        ["YT"] = "YouTrack",
        ["TBA"] = "Toolbox App",
        ["ALL"] = "All Products Pack"
    };

    public IReadOnlyList<LicenseRecord> NormalizeLicenses(IEnumerable<ImportedAccountRow> rows)
    {
        return (rows ?? []).Select(row => new LicenseRecord
        {
            Id = AccountNormalization.BuildId(ProviderId, "license",
                AccountNormalization.ResolveName(row.Product, ProductCodes, "product")),
            Provider = ProviderId,
            Product = AccountNormalization.ResolveName(row.Product, ProductCodes, "product"),
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
