using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.OnlineMetadata.Parsing;

namespace SkyCD.Plugin.Metadata.AniDb.Parsing;

/// <summary>Applies the shared meta / JSON-LD extraction, then fills gaps from AniDB's infobox lists.</summary>
internal static class AniDbPageParser
{
    public static void Parse(string body, DescriptionFacts facts)
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
