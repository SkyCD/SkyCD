using System.IO.Compression;
using System.Xml.Linq;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class RenoiseReader
{
    internal static bool Matches(ReadOnlySpan<byte> data, string extension) =>
        extension == ".xrns" && data.StartsWith("PK\x03\x04"u8);

    internal static SequencerFacts? Read(byte[] bytes, CancellationToken token)
    {
        using var input = new MemoryStream(bytes);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var entry = archive.GetEntry("Song.xml");
        const int limit = 32 * 1024 * 1024;
        if (entry is null || entry.Length > limit)
            return null;
        using var stream = entry.Open();
        var root = ProjectXml.Read(stream, limit, token);
        if (root.Name != "RenoiseSong")
            return null;

        var facts = new SequencerFacts("Module", "xrns");
        var global = root.Element("GlobalSongData");
        var tracks = root.Element("Tracks")?.Elements();
        facts.Text("Application", "Renoise");
        facts.Text("ApplicationVersion", (string?)root.Attribute("doc_version"));
        facts.Text("Title", (string?)global?.Element("SongName"));
        facts.Text("Artist", (string?)global?.Element("Artist"));
        facts.Text("Genre", (string?)global?.Element("Genre"));
        ProjectXml.SetTempo(facts, (string?)global?.Element("BeatsPerMin"));
        facts.Integer("TrackCount", tracks?.Count() ?? 0);
        facts.Integer("InstrumentCount", Count(root.Element("Instruments"), "Instrument"));
        facts.Integer("PatternCount", Count(root.Element("PatternPool")?.Element("Patterns"), "Pattern"));
        facts.AddNames(tracks?.Select(track => (string?)track.Element("Name")) ?? [], 24);
        return facts;
    }

    private static int Count(XElement? parent, string element) => parent?.Elements(element).Count() ?? 0;
}
