using System;
using System.Linq;
using AngleSharp.Dom;
using SkyCD.Plugin.Ai.GenDesc.Import;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Sources;

/// <summary>
/// Imports film and series facts from IMDb. A pasted <c>/title/tt...</c> URL is fetched directly; otherwise the
/// title search endpoint is queried. Facts come primarily from the embedded JSON-LD and Open Graph data that
/// IMDb serves, with a light genre-anchor pass layered on when those are absent.
/// </summary>
public sealed class ImdbImporter : IWebSourceImporter
{
    public string SourceId => "imdb";

    public string? ResolveUrl(ImportRequest request)
    {
        if (UrlHelpers.MatchesHost(request.Url, "imdb.com")) return request.Url;
        if (!string.IsNullOrWhiteSpace(request.Query))
            return $"https://www.imdb.com/find/?q={UrlHelpers.Encode(request.Query.Trim())}&s=tt";
        return null;
    }

    public void Parse(string body, ImportRequest request, DescriptionFacts facts)
    {
        var doc = PageMeta.Parse(body);
        if (doc is null) return;
        PageMeta.Apply(body, facts);

        if (!facts.Has("Genre"))
        {
            var genres = doc.QuerySelectorAll("a[href*=\"search/title/?genres=\"]")
                .Select(anchor => anchor.TextContent.Trim())
                .Where(text => text.Length > 0 && text.Length < 40)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToArray();
            if (genres.Length > 0) facts.Set("Genre", string.Join("|", genres));
        }

        if (!facts.Has("Title"))
        {
            var heading = doc.QuerySelector("h1")?.TextContent;
            if (!string.IsNullOrWhiteSpace(heading)) facts.Set("Title", heading);
        }
    }
}
