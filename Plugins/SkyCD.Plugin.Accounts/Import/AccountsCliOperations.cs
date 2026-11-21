using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Accounts;
using SkyCD.Plugin.Accounts.Models;

namespace SkyCD.Plugin.Accounts.Import;

/// <summary>
/// Shared, host-independent implementation behind the account CLI commands.
/// </summary>
public static class AccountsCliOperations
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ImportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static string ResolveStorePath(string? storePath) =>
        string.IsNullOrWhiteSpace(storePath) ? AccountStore.DefaultPath() : storePath;

    /// <summary>
    /// Runs a provider sync: optionally imports records, normalizes, dedupes and persists.
    /// </summary>
    public static async Task<int> SyncAsync(
        IAccountSyncProvider provider,
        string? fromFile,
        string? storePath,
        bool jsonOutput,
        TextWriter stdout,
        TextWriter stderr)
    {
        var store = new AccountStore(ResolveStorePath(storePath));
        AccountData data;
        try
        {
            data = store.Load();
        }
        catch (Exception exception)
        {
            await stderr.WriteLineAsync($"Cannot load account store: {exception.Message}");
            return 3;
        }

        var imported = new List<ImportedAccountRow>();
        if (!string.IsNullOrWhiteSpace(fromFile))
        {
            if (!File.Exists(fromFile))
            {
                await stderr.WriteLineAsync($"Import file not found: {fromFile}");
                return 2;
            }

            try
            {
                imported = await ReadImportFileAsync(fromFile);
            }
            catch (Exception exception)
            {
                await stderr.WriteLineAsync($"Cannot import '{fromFile}': {exception.Message}");
                return 2;
            }
        }

        var licenses = provider.NormalizeLicenses(imported);
        var subscriptions = provider.NormalizeSubscriptions(imported);
        var (added, updated) = store.Upsert(data, licenses, subscriptions);
        data.LastSyncedUtcByProvider[provider.ProviderId] = DateTimeOffset.UtcNow.ToString("o");

        try
        {
            store.Save(data);
        }
        catch (Exception exception)
        {
            await stderr.WriteLineAsync($"Cannot save account store: {exception.Message}");
            return 3;
        }

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                command = $"sync {provider.ProviderId}",
                provider = provider.ProviderId,
                added,
                updated,
                licenses = data.Licenses.Count,
                subscriptions = data.Subscriptions.Count,
                store = storePath ?? AccountStore.DefaultPath(),
                lastSyncedUtc = data.LastSyncedUtcByProvider[provider.ProviderId]
            }, JsonOptions));
        }
        else
        {
            await stdout.WriteLineAsync(
                $"Synced {provider.DisplayName}: {added} added, {updated} updated " +
                $"({data.Licenses.Count} licenses, {data.Subscriptions.Count} subscriptions).");
        }

        return 0;
    }

    /// <summary>
    /// Lists licenses or subscriptions in human or JSON form.
    /// </summary>
    public static async Task<int> ListAsync(
        string kind,
        string? storePath,
        bool jsonOutput,
        TextWriter stdout,
        TextWriter stderr)
    {
        AccountData data;
        try
        {
            data = new AccountStore(ResolveStorePath(storePath)).Load();
        }
        catch (Exception exception)
        {
            await stderr.WriteLineAsync($"Cannot load account store: {exception.Message}");
            return 3;
        }

        var now = DateTimeOffset.UtcNow;
        if (jsonOutput)
        {
            object payload = kind == "subscriptions"
                ? new { subscriptions = data.Subscriptions.Select(s => SubscriptionView(s, now)).ToArray() }
                : new { licenses = data.Licenses.Select(l => LicenseView(l, now)).ToArray() };
            await stdout.WriteLineAsync(JsonSerializer.Serialize(payload, JsonOptions));
            return 0;
        }

        if (kind == "subscriptions")
        {
            if (data.Subscriptions.Count == 0)
            {
                await stdout.WriteLineAsync("No subscriptions recorded.");
                return 0;
            }

            await stdout.WriteLineAsync("ID | PROVIDER | PRODUCT | SEATS | RENEWS | STATUS");
            foreach (var subscription in data.Subscriptions)
            {
                await stdout.WriteLineAsync(
                    $"{subscription.Id} | {subscription.Provider} | {subscription.Product} | " +
                    $"{subscription.Seats} | {FormatDate(subscription.RenewsUtc)} | {subscription.EvaluateStatus(now)}");
            }

            return 0;
        }

        if (data.Licenses.Count == 0)
        {
            await stdout.WriteLineAsync("No licenses recorded.");
            return 0;
        }

        await stdout.WriteLineAsync("ID | PROVIDER | PRODUCT | SEATS | EXPIRES | STATUS");
        foreach (var license in data.Licenses)
        {
            await stdout.WriteLineAsync(
                $"{license.Id} | {license.Provider} | {license.Product} | " +
                $"{license.Seats} | {FormatDate(license.ExpiresUtc)} | {license.EvaluateStatus(now)}");
        }

        return 0;
    }

    /// <summary>
    /// Exports the full inventory as JSON to standard output or a file.
    /// </summary>
    public static async Task<int> ExportInventoryAsync(
        string? storePath,
        string? outFile,
        TextWriter stdout,
        TextWriter stderr)
    {
        AccountData data;
        try
        {
            data = new AccountStore(ResolveStorePath(storePath)).Load();
        }
        catch (Exception exception)
        {
            await stderr.WriteLineAsync($"Cannot load account store: {exception.Message}");
            return 3;
        }

        var now = DateTimeOffset.UtcNow;
        var payload = new
        {
            version = 1,
            exportedAtUtc = now,
            licenses = data.Licenses.Select(l => LicenseView(l, now)).ToArray(),
            subscriptions = data.Subscriptions.Select(s => SubscriptionView(s, now)).ToArray()
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        if (string.IsNullOrWhiteSpace(outFile))
        {
            await stdout.WriteLineAsync(json);
            return 0;
        }

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(outFile));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(outFile, json);
        }
        catch (Exception exception)
        {
            await stderr.WriteLineAsync($"Cannot write '{outFile}': {exception.Message}");
            return 3;
        }

        await stdout.WriteLineAsync(
            $"Wrote inventory to {outFile} ({data.Licenses.Count} licenses, {data.Subscriptions.Count} subscriptions).");
        return 0;
    }

    private static object LicenseView(LicenseRecord license, DateTimeOffset now) => new
    {
        id = license.Id,
        provider = license.Provider,
        product = license.Product,
        seats = license.Seats,
        expiresUtc = license.ExpiresUtc,
        status = license.EvaluateStatus(now),
        notes = license.Notes
    };

    private static object SubscriptionView(SubscriptionRecord subscription, DateTimeOffset now) => new
    {
        id = subscription.Id,
        provider = subscription.Provider,
        product = subscription.Product,
        seats = subscription.Seats,
        renewsUtc = subscription.RenewsUtc,
        status = subscription.EvaluateStatus(now),
        notes = subscription.Notes
    };

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd") ?? "-";

    private static async Task<List<ImportedAccountRow>> ReadImportFileAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var rows = JsonSerializer.Deserialize<List<ImportedAccountRow>>(json, ImportOptions)
            ?? throw new InvalidOperationException("Import file must contain a JSON array of records.");

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Product))
            {
                throw new InvalidOperationException("Every imported record must set 'product'.");
            }

            if (row.Seats < 0)
            {
                throw new InvalidOperationException($"Record '{row.Product}' has negative seats.");
            }
        }

        return rows;
    }
}
