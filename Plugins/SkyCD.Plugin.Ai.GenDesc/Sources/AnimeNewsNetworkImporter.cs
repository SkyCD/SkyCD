using AngleSharp.Dom;
using SkyCD.Plugin.Ai.GenDesc.Import;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Sources;

/// <summary>
/// Imports anime facts from the Anime News Network encyclopedia. A pasted <c>anime.php?id=</c> URL is fetched
/// directly; otherwise the encyclopedia search is queried. It uses the shared meta/JSON-LD extraction plus ANN's
/// two-column profile table, whose label rows give type, episodes, runtime, studio and staff.
/// </summary>
public sealed class AnimeNewsNetworkImporter : IWebSourceImporter
{
    public string SourceId => "ann";

    public string? ResolveUrl(ImportRequest request)
    {
        if (UrlHelpers.MatchesHost(request.Url, "animenewsnetwork.com")) return request.Url;
        if (!string.IsNullOrWhiteSpace(request.Query))
            return $"https://www.animenewsnetwork.com/encyclopedia/search.php?query={UrlHelpers.Encode(request.Query.Trim())}";
        return null;
    }

    public void Parse(string body, ImportRequest request, DescriptionFacts facts)
    {
        var doc = PageMeta.Parse(body);
        if (doc is null) return;
        PageMeta.Apply(body, facts);

        var box = Infobox.Read(doc);
        if (!facts.Has("Type")) facts.Set("Type", Infobox.Find(box, "type", "format"));
        if (!facts.Has("Episodes")) facts.Set("Episodes", Infobox.Find(box, "episodes", "episodesperseason"));
        if (!facts.Has("Runtime")) facts.Set("Runtime", Infobox.Find(box, "length", "runtime"));
        if (!facts.Has("Studio")) facts.Set("Studio", Infobox.Find(box, "studio", "licensor", "producer"));
        if (!facts.Has("Director")) facts.Set("Director", Infobox.Find(box, "director", "director/assistant"));
        if (!facts.Has("Plot")) facts.Set("Plot", Infobox.Find(box, "plotsummary", "description"));
    }
}
