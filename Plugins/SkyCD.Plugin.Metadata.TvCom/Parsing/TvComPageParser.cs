using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.OnlineMetadata.Parsing;

namespace SkyCD.Plugin.Metadata.TvCom.Parsing;

/// <summary>Applies the shared meta / JSON-LD extraction with heading and summary fallbacks.</summary>
internal static class TvComPageParser
{
    public static void Parse(string body, DescriptionFacts facts)
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
