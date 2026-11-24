using System.Linq;
using AngleSharp.Dom;
using SkyCD.Plugin.Ai.GenDesc.Import;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Sources;

/// <summary>
/// Imports television facts from TV.com (and the CBS show pages it now points to). A pasted show URL is fetched
/// directly; otherwise the site search is queried. It relies on the shared Open Graph and JSON-LD extraction,
/// which those pages expose, with an <c>&lt;h1&gt;</c> title fallback when neither is present.
/// </summary>
public sealed class TvComImporter : IWebSourceImporter
{
    public string SourceId => "tvcom";

    public string? ResolveUrl(ImportRequest request)
    {
        if (UrlHelpers.MatchesHost(request.Url, "tv.com", "cbs.com")) return request.Url;
        if (!string.IsNullOrWhiteSpace(request.Query))
            return $"https://www.tv.com/search/?q={UrlHelpers.Encode(request.Query.Trim())}";
        return null;
    }

    public void Parse(string body, ImportRequest request, DescriptionFacts facts)
    {
        var doc = PageMeta.Parse(body);
        if (doc is null) return;
        PageMeta.Apply(body, facts);

        if (!facts.Has("Title"))
        {
            var heading = doc.QuerySelector("h1")?.TextContent?.Trim();
            if (!string.IsNullOrWhiteSpace(heading) && heading.Length < 200) facts.Set("Title", heading);
        }
        if (!facts.Has("Plot"))
        {
            var summary = doc.QuerySelector("[itemprop=\"description\"], .show-summary, .description")?.TextContent;
            if (!string.IsNullOrWhiteSpace(summary)) facts.Set("Plot", summary);
        }
    }
}
