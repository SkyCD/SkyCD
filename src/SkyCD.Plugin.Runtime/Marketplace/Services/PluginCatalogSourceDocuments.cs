using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using SkyCD.Plugin.Runtime.Marketplace.Exceptions;
using SkyCD.Plugin.Runtime.Marketplace.Models;

namespace SkyCD.Plugin.Runtime.Marketplace.Services;

/// <summary>Reading rules for the two URL-bearing documents: the catalog list and source URLs.</summary>
internal static class PluginCatalogSourceDocuments
{
    /// <summary>
    /// Reads the catalog document: a JSON array of source URL strings, <c>[]</c> means no sources. Invalid
    /// entries are returned as-is so the resolver can reject them individually.
    /// </summary>
    public static IReadOnlyList<string> ParseEntries(Uri catalogEndpoint, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                throw new MalformedPluginCatalogException(catalogEndpoint);
            }

            var entries = new List<string>();
            foreach (var element in root.EnumerateArray())
            {
                entries.Add(element.ValueKind == JsonValueKind.String
                    ? (element.GetString() ?? string.Empty).Trim()
                    : element.GetRawText());
            }

            return entries;
        }
        catch (JsonException)
        {
            throw new MalformedPluginCatalogException(catalogEndpoint);
        }
    }

    /// <summary>Accepts only absolute HTTPS URLs without credentials.</summary>
    public static bool TryNormalize(string entry, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(entry, UriKind.Absolute, out var parsed) || !IsHttps(parsed))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    public static bool IsHttps(Uri uri)
    {
        return uri.IsAbsoluteUri &&
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrEmpty(uri.UserInfo);
    }

    /// <summary>A stable hash of the manifest text, used only for change detection and diagnostics.</summary>
    public static string Fingerprint(string text)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16];
    }

    /// <summary>A readable stand-in address for an entry that is not an absolute URL.</summary>
    public static Uri PlaceholderFor(string entry)
    {
        var safe = new string(entry.Select(static character =>
                char.IsLetterOrDigit(character) || character is '-' or '.' or '_' ? character : '_').ToArray());
        if (safe.Length > 120)
        {
            safe = safe[..120];
        }

        return new Uri($"https://invalid.invalid/rejected/{(safe.Length == 0 ? "empty" : safe)}");
    }
}
