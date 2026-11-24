using AngleSharp.Dom;
using SkyCD.Plugin.Ai.GenDesc.Import;
using SkyCD.Plugin.Ai.GenDesc.Models;

namespace SkyCD.Plugin.Ai.GenDesc.Sources;

/// <summary>
/// Imports anime facts from AniDB. A pasted <c>/anime/...</c> URL is fetched directly; otherwise the site search
/// is queried. It combines the shared JSON-LD / Open Graph extraction with AniDB's infobox definition list,
/// which carries type, episodes, runtime, studio and staff that the structured data often omits.
/// </summary>
public sealed class AniDbImporter : IWebSourceImporter
{
    public string SourceId => "anidb";

    public string? ResolveUrl(ImportRequest request)
    {
        if (UrlHelpers.MatchesHost(request.Url, "anidb.net", "anidb.info")) return request.Url;
        if (!string.IsNullOrWhiteSpace(request.Query))
            return $"https://anidb.net/search/?q={UrlHelpers.Encode(request.Query.Trim())}";
        return null;
    }

    public void Parse(string body, ImportRequest request, DescriptionFacts facts)
    {
        var doc = PageMeta.Parse(body);
        if (doc is null) return;
        PageMeta.Apply(body, facts);

        var box = Infobox.Read(doc);
        if (!facts.Has("Type")) facts.Set("Type", Infobox.Find(box, "type", "format"));
        if (!facts.Has("Episodes")) facts.Set("Episodes", Infobox.Find(box, "episodes", "episodecount"));
        if (!facts.Has("Runtime")) facts.Set("Runtime", Infobox.Find(box, "length", "runtime", "duration"));
        if (!facts.Has("Studio")) facts.Set("Studio", Infobox.Find(box, "studio", "producer", "production"));
        if (!facts.Has("Director")) facts.Set("Director", Infobox.Find(box, "director"));
        if (!facts.Has("Plot")) facts.Set("Plot", Infobox.Find(box, "description", "synopsis", "story"));
        if (!facts.Has("Genre")) facts.Set("Genre", Infobox.Find(box, "tags", "genres", "theme"));
    }
}
