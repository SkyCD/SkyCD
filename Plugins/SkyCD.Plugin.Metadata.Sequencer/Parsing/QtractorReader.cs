using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class QtractorReader
{
    internal static SequencerFacts? Read(byte[] bytes, CancellationToken token)
    {
        var root = ProjectXml.Read(bytes, token);
        var facts = ProjectXml.CreateFacts(root, "session", "qtr", "Qtractor");
        if (facts is null)
            return null;

        facts.Integer("TrackCount", root.Element("tracks")?.Elements("track").Count() ?? 0);
        ProjectXml.SetTempo(facts, (string?)root.Element("properties")?.Element("tempo"));
        return facts;
    }
}
