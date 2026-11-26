using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.SDK.Capabilities.Accounts;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Accounts;

/// <summary>A catalog entry planned for one owned license or subscription record.</summary>
public sealed record AccountEntryPlan
{
    public required string Id { get; init; }
    public required string RecordId { get; init; }
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required string Product { get; init; }
    public string? ProductCode { get; init; }
    public string? LicenseType { get; init; }
    public int Seats { get; init; }
    public DateTimeOffset? ExpiresUtc { get; init; }
    public DateTimeOffset? RenewsUtc { get; init; }
    public bool RenewsAutomatically { get; init; }
    public string? AssigneeEmail { get; init; }
    public required string Status { get; init; }
    public string? ExecutablePath { get; init; }
}

/// <summary>The stable account folder and its one-entry-per-record children.</summary>
public sealed record AccountFolderPlan
{
    public required string FolderId { get; init; }
    public required string FolderName { get; init; }
    public required string ProviderId { get; init; }
    public required string AccountKey { get; init; }
    public string? AccountEmail { get; init; }
    public required IReadOnlyList<AccountEntryPlan> Entries { get; init; }
}

/// <summary>
/// Turns a connector's token-free ownership snapshot into the account folder and its entries.
/// Folder and entry ids depend only on the provider and the record, so re-adding or syncing updates
/// the same documents instead of duplicating them.
/// </summary>
public static class AccountCatalogBuilder
{
    private const string DetailSeparator = " · ";

    public static AccountFolderPlan BuildPlan(AccountSessionStatus status, DateTimeOffset? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(status);

        var now = asOf ?? DateTimeOffset.UtcNow;
        var providerId = SlugOr(status.ProviderId, "account");
        var accountKey = string.IsNullOrWhiteSpace(status.AccountId) ? "default" : status.AccountId.Trim();
        var accountDisplay = string.IsNullOrWhiteSpace(status.AccountEmail)
            ? accountKey
            : status.AccountEmail.Trim();
        var folderName = string.IsNullOrWhiteSpace(status.DisplayName)
            ? accountDisplay
            : LocalizationService.Current.Format(UiKeys.AccountFolderName, status.DisplayName.Trim(),
                accountDisplay);
        var folderId = $"account-folder-{providerId}-{SlugOr(accountKey, "default")}";

        var items = status.Products
            .Select(product => CreateItem(providerId, product, now))
            .OrderBy(item => item.Product, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RecordId, StringComparer.Ordinal)
            .ToList();
        AssignUniqueIds(items);
        AssignNames(items);

        return new AccountFolderPlan
        {
            FolderId = folderId,
            FolderName = folderName,
            ProviderId = providerId,
            AccountKey = accountKey,
            AccountEmail = string.IsNullOrWhiteSpace(status.AccountEmail) ? null : status.AccountEmail.Trim(),
            Entries = items.Select(ToEntryPlan).ToList()
        };
    }

    private static AccountEntryPlan ToEntryPlan(AccountItem item) => new()
    {
        Id = item.Id,
        RecordId = item.RecordId,
        Name = item.Name,
        Detail = item.Detail,
        Product = item.Product,
        ProductCode = item.ProductCode,
        LicenseType = item.LicenseType,
        Seats = item.Seats,
        ExpiresUtc = item.ExpiresUtc,
        RenewsUtc = item.RenewsUtc,
        RenewsAutomatically = item.RenewsAutomatically,
        AssigneeEmail = item.AssigneeEmail,
        Status = item.Status,
        ExecutablePath = item.ExecutablePath
    };

    public static IReadOnlyList<CatalogDocument> BuildDocuments(AccountFolderPlan plan, string? parentId)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var effectiveParentId = string.IsNullOrWhiteSpace(parentId) ? null : parentId;
        var folderProperties = new PropertiesCollection
        {
            [CatalogDocument.SizeKnownPropertyKey] = false,
            ["AccountProvider"] = plan.ProviderId,
            ["AccountKey"] = plan.AccountKey
        };
        if (plan.AccountEmail is { } email)
        {
            folderProperties["AccountEmail"] = email;
        }

        var documents = new List<CatalogDocument>(plan.Entries.Count + 1)
        {
            new()
            {
                Id = plan.FolderId,
                Name = plan.FolderName,
                ParentId = effectiveParentId,
                Type = CatalogDocumentType.Folder,
                Size = 0,
                ChildrenCount = 0,
                Properties = folderProperties
            }
        };

        foreach (var entry in plan.Entries)
        {
            var properties = new PropertiesCollection
            {
                [CatalogDocument.SizeKnownPropertyKey] = false,
                ["AccountProvider"] = plan.ProviderId,
                ["AccountKey"] = plan.AccountKey,
                ["AccountRecordId"] = entry.RecordId,
                ["Product"] = entry.Product,
                ["LicenseStatus"] = entry.Status,
                ["Seats"] = entry.Seats
            };
            AddIfPresent(properties, "ProductCode", entry.ProductCode);
            AddIfPresent(properties, "LicenseType", entry.LicenseType);
            AddIfPresent(properties, "AssignedTo", entry.AssigneeEmail);
            AddIfPresent(properties, "ExpiresUtc", entry.ExpiresUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            AddIfPresent(properties, "RenewsUtc", entry.RenewsUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            AddIfPresent(properties, "FullName", entry.ExecutablePath);
            if (entry.RenewsAutomatically)
            {
                properties["RenewsAutomatically"] = true;
            }

            documents.Add(new CatalogDocument
            {
                Id = entry.Id,
                Name = entry.Name,
                ParentId = plan.FolderId,
                Type = CatalogDocumentType.File,
                Size = 0,
                ChildrenCount = 0,
                Properties = properties
            });
        }

        return documents;
    }

    private static AccountItem CreateItem(string providerId, AccountOwnedProduct product, DateTimeOffset asOf)
    {
        var productName = string.IsNullOrWhiteSpace(product.Product)
            ? EmptyToNull(product.ProductCode) ?? LocalizationService.Current.Get(UiKeys.AccountUnknownProduct)
            : product.Product.Trim();
        var recordId = string.IsNullOrWhiteSpace(product.Id)
            ? AccountNormalization.BuildId(providerId, "record",
                string.Join('|', productName, product.ProductCode, product.LicenseType, product.ExpiresUtc?.ToString("O"),
                    product.RenewsUtc?.ToString("O"), product.AssigneeEmail, product.Seats))
            : product.Id.Trim();
        var status = string.IsNullOrWhiteSpace(product.Status) ? "unknown" : product.Status.Trim();

        return new AccountItem
        {
            BaseId = $"account-{providerId}-{recordId}",
            RecordId = recordId,
            Product = productName,
            Detail = BuildDetail(product, status),
            ProductCode = EmptyToNull(product.ProductCode),
            LicenseType = EmptyToNull(product.LicenseType),
            Seats = product.Seats,
            ExpiresUtc = product.ExpiresUtc,
            RenewsUtc = product.RenewsUtc,
            RenewsAutomatically = product.RenewsAutomatically,
            AssigneeEmail = EmptyToNull(product.AssigneeEmail),
            Status = status,
            ExecutablePath = EmptyToNull(product.ExecutablePath)
        };
    }

    private static string BuildDetail(AccountOwnedProduct product, string status)
    {
        var parts = new List<string> { AccountStatusText.Get(status) };
        parts.Add(LocalizationService.Current.Format(UiKeys.AccountEntrySeats, product.Seats));
        if (product.ExpiresUtc is { } expires)
        {
            parts.Add(LocalizationService.Current.Format(UiKeys.AccountEntryExpiresLabel, FormatDate(expires)));
        }

        if (product.RenewsUtc is { } renews)
        {
            parts.Add(LocalizationService.Current.Format(UiKeys.AccountEntryRenewsLabel, FormatDate(renews)));
        }

        if (!string.IsNullOrWhiteSpace(product.AssigneeEmail))
        {
            parts.Add(LocalizationService.Current.Format(UiKeys.AccountEntryAssignedToLabel,
                product.AssigneeEmail.Trim()));
        }

        return string.Join(DetailSeparator, parts);
    }

    private static void AssignUniqueIds(List<AccountItem> items)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var id = item.BaseId;
            var suffix = 2;
            while (!used.Add(id))
            {
                id = $"{item.BaseId}-{suffix++}";
            }

            item.Id = id;
        }
    }

    private static void AssignNames(List<AccountItem> items)
    {
        foreach (var group in items.GroupBy(item => item.Product, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            if (members.Count == 1)
            {
                members[0].Name = members[0].Product;
                continue;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unresolved = new List<AccountItem>();
            foreach (var item in members)
            {
                var qualifier = PickUniqueQualifier(item, members);
                if (qualifier is null)
                {
                    unresolved.Add(item);
                    continue;
                }

                item.Name = LocalizationService.Current.Format(UiKeys.AccountEntryNameFormat, item.Product,
                    qualifier);
                used.Add(item.Name);
            }

            for (var index = 0; index < unresolved.Count; index++)
            {
                var item = unresolved[index];
                var ordinal = index + 2;
                var name = LocalizationService.Current.Format(UiKeys.AccountEntryOrdinal, item.Product, ordinal);
                while (!used.Add(name))
                {
                    name = LocalizationService.Current.Format(UiKeys.AccountEntryOrdinal, item.Product, ++ordinal);
                }

                item.Name = name;
            }
        }
    }

    private static string? PickUniqueQualifier(AccountItem item, IReadOnlyList<AccountItem> members)
    {
        if (item.AssigneeEmail is { } email &&
            CountMatching(members, candidate => candidate.AssigneeEmail, email) == 1)
        {
            return LocalizationService.Current.Format(UiKeys.AccountEntryAssignedTo, email);
        }

        if (item.ExpiresUtc is { } expires &&
            CountMatching(members, candidate => candidate.ExpiresUtc, expires) == 1)
        {
            return LocalizationService.Current.Format(UiKeys.AccountEntryExpires, FormatDate(expires));
        }

        if (item.RenewsUtc is { } renews &&
            CountMatching(members, candidate => candidate.RenewsUtc, renews) == 1)
        {
            return LocalizationService.Current.Format(UiKeys.AccountEntryRenews, FormatDate(renews));
        }

        return null;
    }

    private static int CountMatching<T>(
        IReadOnlyList<AccountItem> members,
        Func<AccountItem, T?> selector,
        T value)
        where T : struct =>
        members.Count(member => selector(member) is { } candidate && EqualityComparer<T>.Default.Equals(candidate, value));

    private static int CountMatching(
        IReadOnlyList<AccountItem> members,
        Func<AccountItem, string?> selector,
        string value) =>
        members.Count(member => string.Equals(selector(member), value, StringComparison.OrdinalIgnoreCase));

    private static string FormatDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string SlugOr(string? value, string fallback)
    {
        var slug = string.IsNullOrWhiteSpace(value) ? string.Empty : AccountNormalization.Slug(value);
        return string.IsNullOrWhiteSpace(slug) ? fallback : slug;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddIfPresent(PropertiesCollection properties, string key, object? value)
    {
        if (value is not null)
        {
            properties[key] = value;
        }
    }

    private sealed class AccountItem
    {
        public required string BaseId { get; init; }
        public required string RecordId { get; init; }
        public required string Product { get; init; }
        public required string Detail { get; init; }
        public string? ProductCode { get; init; }
        public string? LicenseType { get; init; }
        public int Seats { get; init; }
        public DateTimeOffset? ExpiresUtc { get; init; }
        public DateTimeOffset? RenewsUtc { get; init; }
        public bool RenewsAutomatically { get; init; }
        public string? AssigneeEmail { get; init; }
        public required string Status { get; init; }
        public string? ExecutablePath { get; init; }
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }
}
