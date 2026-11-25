using System;
using System.Linq;
using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.OnlineMetadata.Parsing;

namespace SkyCD.Plugin.Metadata.Imdb.Parsing;

/// <summary>Applies the shared meta / JSON-LD extraction and IMDb's own genre anchors and heading fallback.</summary>
internal static class ImdbPageParser
{
    public static void Parse(string body, DescriptionFacts facts)
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
