using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class AbletonReader
{
    internal static SequencerFacts? Read(byte[] bytes, CancellationToken token)
    {
        var root = ProjectXml.Read(bytes, token, allowGzip: true, limitMiB: 64);
        var facts = ProjectXml.CreateFacts(root, "Ableton", "als", "Ableton Live");
        if (facts is null)
            return null;

        var live = root.Element("LiveSet");
        if (live is null)
            return null;
        if (root.Attribute("Creator") is null)
            facts.Text("ApplicationVersion", string.Join(".", new[] { ProjectXml.Attribute(root, "MajorVersion"),
                ProjectXml.Attribute(root, "MinorVersion") }.Where(value => value is not null)));
        var tracks = live.Element("Tracks")?.Elements().ToArray() ?? [];
        facts.Integer("TrackCount", tracks.Length);
        facts.Integer("AudioTrackCount", tracks.Count(t => t.Name == "AudioTrack"));
        facts.Integer("MidiTrackCount", tracks.Count(t => t.Name == "MidiTrack"));
        facts.Integer("ReturnTrackCount", tracks.Count(t => t.Name == "ReturnTrack"));
        facts.Integer("SceneCount", live.Element("Scenes")?.Elements("Scene").Count() ?? 0);
        facts.Integer("ClipCount", live.Descendants("ClipSlot").Count(e => e.Attribute("Id") is not null));
        facts.Integer("MediaFileCount", live.Descendants("FileRef").Count());
        facts.AddNames(tracks.Select(t => (string?)t.Element("Name")?.Element("EffectiveName")?.Attribute("Value")), 24);
        ProjectXml.SetTempo(facts, ProjectXml.Attribute(live.Descendants("Tempo").FirstOrDefault()?.Element("Manual"), "Value"));
        return facts;
    }
}
