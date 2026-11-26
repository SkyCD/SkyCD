using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class LmmsReader
{
    internal static SequencerFacts? Read(byte[] bytes, CancellationToken token, bool compressed)
    {
        var root = ProjectXml.Read(bytes, token, allowGzip: compressed, allowQtZlib: compressed);
        var facts = ProjectXml.CreateFacts(root, "lmms", "mmp", "LMMS");
        if (facts is null)
            return null;

        var container = root.Element("song")?.Element("trackcontainer");
        facts.Integer("TrackCount", container?.Elements("track").Count() ?? 0);
        facts.Integer("InstrumentTrackCount", container?.Elements("track").Count(t => t.Element("instrumenttrack") is not null) ?? 0);
        ProjectXml.SetTempo(facts, ProjectXml.Attribute(root.Element("head"), "bpm"));
        return facts;
    }
}
