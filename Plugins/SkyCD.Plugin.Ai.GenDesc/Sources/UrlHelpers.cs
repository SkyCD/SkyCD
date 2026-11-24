using System;
using System.Linq;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Sources;

/// <summary>Small shared helpers for deciding which importer owns a URL and for encoding query values.</summary>
internal static class UrlHelpers
{
    /// <summary>True when <paramref name="url"/> is an absolute http(s) URL whose host ends with any given domain.</summary>
    public static bool MatchesHost(string? url, params string[] domains)
    {
        if (!ImportRequest.IsAbsoluteHttpUrl(url)) return false;
        var host = new Uri(url!, UriKind.Absolute).Host;
        return domains.Any(domain =>
            host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    public static string Encode(string value) => Uri.EscapeDataString(value);

    /// <summary>Extracts the last path segment of a URL, decoded, for sites that key on a trailing path token.</summary>
    public static string LastPathSegment(string url)
    {
        if (!ImportRequest.IsAbsoluteHttpUrl(url)) return string.Empty;
        var segments = new Uri(url, UriKind.Absolute).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 0 ? string.Empty : Uri.UnescapeDataString(segments[^1]);
    }
}
