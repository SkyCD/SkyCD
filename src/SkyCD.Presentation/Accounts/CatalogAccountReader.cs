using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Collections;

namespace SkyCD.Presentation.Accounts;

/// <summary>Token-free account folder descriptor read from the open catalog.</summary>
public sealed record CatalogAccountDescriptor
{
    public required string FolderId { get; init; }

    public required string FolderName { get; init; }

    public required string ProviderId { get; init; }

    public required string AccountKey { get; init; }

    public string? AccountEmail { get; init; }

    public string? DisplayName { get; init; }

    public bool IsConnected { get; init; } = true;

    public DateTimeOffset? LastSyncedUtc { get; init; }
}

/// <summary>
/// Reads per-catalog account folders from catalog documents. The folder properties are the
/// catalog's record of the connection; tokens never appear here (see secrets boundary).
/// </summary>
public static class CatalogAccountReader
{
    public const string ProviderKey = "AccountProvider";
    public const string KeyKey = "AccountKey";
    public const string EmailKey = "AccountEmail";
    public const string DisplayNameKey = "AccountDisplayName";
    public const string ConnectedKey = "AccountConnected";
    public const string LastSyncKey = "AccountLastSync";

    private static readonly string[] CredentialKeyFragments =
    [
        "token",
        "refreshtoken",
        "refresh_token",
        "access_token",
        "accesstoken",
        "secret",
        "password",
        "authorization",
        "bearer",
        "privatekey",
        "private_key",
        "clientsecret",
        "client_secret",
    ];

    public static IReadOnlyList<CatalogAccountDescriptor> ReadAccountFolders(
        IEnumerable<CatalogDocument>? documents)
    {
        var result = new List<CatalogAccountDescriptor>();
        if (documents is null)
        {
            return result;
        }

        foreach (var document in documents)
        {
            if (document.Type != Documents.Enum.CatalogDocumentType.Folder)
            {
                continue;
            }

            var properties = document.Properties;
            if (properties is null)
            {
                continue;
            }

            if (!properties.TryGetValue(ProviderKey, out var providerValue) ||
                providerValue is not string providerId ||
                string.IsNullOrWhiteSpace(providerId))
            {
                continue;
            }

            if (!properties.TryGetValue(KeyKey, out var keyValue) ||
                keyValue is not string accountKey ||
                string.IsNullOrWhiteSpace(accountKey))
            {
                continue;
            }

            properties.TryGetValue(EmailKey, out var emailValue);
            properties.TryGetValue(DisplayNameKey, out var displayNameValue);
            properties.TryGetValue(ConnectedKey, out var connectedValue);
            properties.TryGetValue(LastSyncKey, out var lastSyncValue);
            var descriptor = new CatalogAccountDescriptor
            {
                FolderId = document.Id,
                FolderName = document.Name,
                ProviderId = providerId.Trim(),
                AccountKey = accountKey.Trim(),
                AccountEmail = emailValue as string,
                DisplayName = displayNameValue as string,
                IsConnected = connectedValue is bool connected ? connected : true,
                LastSyncedUtc = ParseLastSync(lastSyncValue as string),
            };
            result.Add(descriptor);
        }

        return result
            .OrderBy(descriptor => descriptor.FolderName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static bool ContainsCredentialMaterial(CatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return ContainsCredentialMaterial(document.Properties);
    }

    public static bool ContainsCredentialMaterial(PropertiesCollection? properties)
    {
        if (properties is null)
        {
            return false;
        }

        foreach (var key in properties.Keys.OfType<string>())
        {
            var normalized = key.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
            if (CredentialKeyFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal)))
            {
                return true;
            }

            if (properties[key] is string value && LooksLikeTokenValue(key, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeTokenValue(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 16)
        {
            return false;
        }

        var normalizedKey = key.ToLowerInvariant();
        if (normalizedKey.Contains("token", StringComparison.Ordinal) ||
            normalizedKey.Contains("secret", StringComparison.Ordinal) ||
            normalizedKey.Contains("password", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static DateTimeOffset? ParseLastSync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }
}
