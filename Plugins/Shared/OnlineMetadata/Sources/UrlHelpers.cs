using System;
using System.Linq;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.OnlineMetadata.Sources;

/// <summary>Small shared helpers for deciding which source owns a URL and for encoding query values.</summary>
internal static class UrlHelpers
{
    /// <summary>True when <paramref name="url"/> is an absolute http(s) URL whose host ends with any given domain.</summary>
    public static bool MatchesHost(string? url, params string[] domains)
    {
        if (!OnlineMetadataRequest.IsAbsoluteHttpUrl(url)) return false;
        var host = new Uri(url!, UriKind.Absolute).Host;
        return domains.Any(domain =>
            host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    public static string Encode(string value) => Uri.EscapeDataString(value);
}
