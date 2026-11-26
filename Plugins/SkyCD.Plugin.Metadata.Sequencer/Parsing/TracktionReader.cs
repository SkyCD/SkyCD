using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class TracktionReader
{
    internal static SequencerFacts? Read(byte[] bytes, CancellationToken token)
    {
        var root = ProjectXml.Read(bytes, token);
        var facts = ProjectXml.CreateFacts(root, "EDIT", "tracktionedit", "Tracktion");
        if (facts is null)
            return null;

        facts.Integer("TrackCount", root.Descendants("TRACK").Count());
        ProjectXml.SetTempo(facts, ProjectXml.Attribute(root.Descendants("TEMPO").FirstOrDefault(), "bpm"));
        return facts;
    }
}
