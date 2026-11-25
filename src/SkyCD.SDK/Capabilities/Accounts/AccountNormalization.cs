#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.SDK.Capabilities.Accounts;

public static class AccountNormalization
{
    public static string Slug(string value)
    {
        var slug = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }

    public static string BuildId(string providerId, string kind, string product)
    {
        var slug = Slug(product);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "unnamed";
        }

        return $"{providerId}:{kind}:{slug}";
    }

    public static string ResolveName(string? product, IReadOnlyDictionary<string, string> codeMap, string fallbackKind)
    {
        if (string.IsNullOrWhiteSpace(product))
        {
            return $"Unknown {fallbackKind}";
        }

        var trimmed = product.Trim();
        return codeMap.TryGetValue(trimmed, out var displayName) ? displayName : trimmed;
    }
}
