using System;

namespace SkyCD.Plugin.Ai.GenDesc.Models;

/// <summary>
/// What an importer is asked to look up: a search query derived from the entry, an optional explicit
/// source URL the user attached, and the language the description should be produced in.
/// </summary>
public sealed record ImportRequest(string Query, string? Url, string Language)
{
    /// <summary>A request is usable when it has either a query or an explicit URL to resolve.</summary>
    public bool HasLookup => !string.IsNullOrWhiteSpace(Query) || IsAbsoluteHttpUrl(Url);

    /// <summary>True only for an http(s) absolute URL, so a malformed value never reaches the fetcher.</summary>
    public static bool IsAbsoluteHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
}
