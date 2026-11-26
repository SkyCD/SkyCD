using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class ArdourReader
{
    internal static SequencerFacts? Read(byte[] bytes, CancellationToken token)
    {
        var root = ProjectXml.Read(bytes, token);
        var facts = ProjectXml.CreateFacts(root, "Session", "ardour", "Ardour");
        if (facts is null)
            return null;

        facts.Integer("TrackCount", root.Descendants("Route").Count(e => e.Attribute("audio-playlist") is not null
            || e.Attribute("midi-playlist") is not null));
        ProjectXml.SetTempo(facts, ProjectXml.Attribute(root.Descendants("Tempo").FirstOrDefault(), "npm", "beats-per-minute", "bpm"));
        return facts;
    }
}
