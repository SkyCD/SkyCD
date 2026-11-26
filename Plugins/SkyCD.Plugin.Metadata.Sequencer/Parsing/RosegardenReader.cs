using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class RosegardenReader
{
    internal static SequencerFacts? Read(byte[] bytes, CancellationToken token)
    {
        var root = ProjectXml.Read(bytes, token, allowGzip: true);
        var facts = ProjectXml.CreateFacts(root, "rosegarden-data", "rg", "Rosegarden");
        if (facts is null)
            return null;

        facts.Integer("TrackCount", root.Element("composition")?.Elements("track").Count() ?? 0);
        ProjectXml.SetTempo(facts, ProjectXml.Attribute(root.Descendants("tempo").FirstOrDefault(), "bph"), scale: 1d / 60);
        return facts;
    }
}
