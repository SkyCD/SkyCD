using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;

namespace SkyCD.Plugin.Accounts.Core.Models;

/// <summary>
/// JSON-file backed ownership/account store. Writes are atomic (temp file + move).
/// </summary>
public sealed class AccountStore
{
    private static readonly JsonSerializerOptions StoreOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string filePath;
    private readonly object gate = new();

    public AccountStore(string filePath)
    {
        this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public static string DefaultPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SkyCD",
            "accounts.json");
    }

    public AccountData Load()
    {
        lock (gate)
        {
            if (!File.Exists(filePath))
            {
                return new AccountData();
            }

            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<AccountData>(json, StoreOptions);
            return data ?? new AccountData();
        }
    }

    public void Save(AccountData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (gate)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(data, StoreOptions));
            File.Move(tempPath, filePath, overwrite: true);
        }
    }

    /// <summary>
    /// Inserts or replaces records by ID.
    /// </summary>
    public (int Added, int Updated) Upsert(
        AccountData data,
        IEnumerable<LicenseRecord> licenses,
        IEnumerable<SubscriptionRecord> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(data);
        var added = 0;
        var updated = 0;

        foreach (var license in licenses ?? [])
        {
            if (data.Licenses.RemoveAll(existing =>
                    existing.Id.Equals(license.Id, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                updated++;
            }
            else
            {
                added++;
            }

            data.Licenses.Add(license);
        }

        foreach (var subscription in subscriptions ?? [])
        {
            if (data.Subscriptions.RemoveAll(existing =>
                    existing.Id.Equals(subscription.Id, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                updated++;
            }
            else
            {
                added++;
            }

            data.Subscriptions.Add(subscription);
        }

        data.Licenses.Sort(static (a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        data.Subscriptions.Sort(static (a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        return (added, updated);
    }
}
