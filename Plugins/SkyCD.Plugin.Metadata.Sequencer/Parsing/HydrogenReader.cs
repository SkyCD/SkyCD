using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class HydrogenReader
{
    internal static SequencerFacts? Read(byte[] bytes, CancellationToken token)
    {
        var root = ProjectXml.Read(bytes, token);
        var facts = ProjectXml.CreateFacts(root, "song", "h2song", "Hydrogen");
        if (facts is null)
            return null;

        facts.Text("Title", (string?)root.Element("name"));
        facts.Text("Artist", (string?)root.Element("author"));
        ProjectXml.SetTempo(facts, (string?)root.Element("bpm"));
        return facts;
    }
}
