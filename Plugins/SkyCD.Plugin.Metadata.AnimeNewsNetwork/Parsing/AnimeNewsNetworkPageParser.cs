using SkyCD.Plugin.OnlineMetadata.Models;
using SkyCD.Plugin.OnlineMetadata.Parsing;

namespace SkyCD.Plugin.Metadata.AnimeNewsNetwork.Parsing;

/// <summary>Applies the shared meta / JSON-LD extraction, then fills gaps from ANN's profile table.</summary>
internal static class AnimeNewsNetworkPageParser
{
    public static void Parse(string body, DescriptionFacts facts)
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
