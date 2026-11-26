using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SkyCD.Plugin.Metadata.Sequencer.Exceptions;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class ProjectReader
{
    private const int MiB = 1024 * 1024;

    internal static SequencerFacts? Read(byte[] bytes, string extension, CancellationToken token)
    {
        if (extension == ".flp")
            return ReadFlp(bytes, token);
        if (extension == ".rpp")
            return ReadReaper(bytes, token);

        using var input = new MemoryStream(bytes);
        using var compressed = OpenCompressedProject(input, bytes, extension);
        var root = ReadXml(compressed ?? input, (extension == ".als" ? 64 : 32) * MiB, token);
        var format = extension.TrimStart('.');
        var (signature, application) = format switch
        {
            "als" => ("Ableton", "Ableton Live"),
            "ardour" => ("Session", "Ardour"),
            "mmp" or "mmpz" => ("lmms", "LMMS"),
            "rg" => ("rosegarden-data", "Rosegarden"),
            "qtr" => ("session", "Qtractor"),
            "tracktionedit" => ("EDIT", "Tracktion"),
            "h2song" => ("song", "Hydrogen"),
            _ => ("", "")
        };
        if (root.Name.LocalName != signature)
            return null;

        var facts = new SequencerFacts("Project", format == "mmpz" ? "mmp" : format);
        facts.Text("Application", application);
        facts.Text("ApplicationVersion", Attribute(root, "Creator", "creatorversion", "applicationVersion", "appVersion", "version")
            ?? (string?)root.Element("version"));
        switch (format)
        {
            case "als":
                var live = root.Element("LiveSet");
                if (live is null)
                    return null;
                if (root.Attribute("Creator") is null)
                    facts.Text("ApplicationVersion", string.Join(".", new[] { Attribute(root, "MajorVersion"),
                        Attribute(root, "MinorVersion") }.Where(value => value is not null)));
                var tracks = live.Element("Tracks")?.Elements().ToArray() ?? [];
                facts.Integer("TrackCount", tracks.Length);
                facts.Integer("AudioTrackCount", tracks.Count(t => t.Name == "AudioTrack"));
                facts.Integer("MidiTrackCount", tracks.Count(t => t.Name == "MidiTrack"));
                facts.Integer("ReturnTrackCount", tracks.Count(t => t.Name == "ReturnTrack"));
                facts.Integer("SceneCount", live.Element("Scenes")?.Elements("Scene").Count() ?? 0);
                facts.Integer("ClipCount", live.Descendants("ClipSlot").Count(e => e.Attribute("Id") is not null));
                facts.Integer("MediaFileCount", live.Descendants("FileRef").Count());
                facts.AddNames(tracks.Select(t => (string?)t.Element("Name")?.Element("EffectiveName")?.Attribute("Value")), 24);
                SetTempo(facts, Attribute(live.Descendants("Tempo").FirstOrDefault()?.Element("Manual"), "Value"));
                break;
            case "ardour":
                facts.Integer("TrackCount", root.Descendants("Route").Count(e => e.Attribute("audio-playlist") is not null
                    || e.Attribute("midi-playlist") is not null));
                SetTempo(facts, Attribute(root.Descendants("Tempo").FirstOrDefault(), "npm", "beats-per-minute", "bpm"));
                break;
            case "mmp":
            case "mmpz":
                var container = root.Element("song")?.Element("trackcontainer");
                facts.Integer("TrackCount", container?.Elements("track").Count() ?? 0);
                facts.Integer("InstrumentTrackCount", container?.Elements("track").Count(t => t.Element("instrumenttrack") is not null) ?? 0);
                SetTempo(facts, Attribute(root.Element("head"), "bpm"));
                break;
            case "rg":
                facts.Integer("TrackCount", root.Element("composition")?.Elements("track").Count() ?? 0);
                SetTempo(facts, Attribute(root.Descendants("tempo").FirstOrDefault(), "bph"), scale: 1d / 60);
                break;
            case "qtr":
                facts.Integer("TrackCount", root.Element("tracks")?.Elements("track").Count() ?? 0);
                SetTempo(facts, (string?)root.Element("properties")?.Element("tempo"));
                break;
            case "tracktionedit":
                facts.Integer("TrackCount", root.Descendants("TRACK").Count());
                SetTempo(facts, Attribute(root.Descendants("TEMPO").FirstOrDefault(), "bpm"));
                break;
            case "h2song":
                facts.Text("Title", (string?)root.Element("name"));
                facts.Text("Artist", (string?)root.Element("author"));
                SetTempo(facts, (string?)root.Element("bpm"));
                break;
        }

        return facts;
    }

    private static Stream? OpenCompressedProject(MemoryStream input, ReadOnlySpan<byte> bytes, string extension)
    {
        if (extension is ".als" or ".mmpz" or ".rg" && bytes.Length >= 2 && bytes[0] == 0x1f && bytes[1] == 0x8b)
            return new GZipStream(input, CompressionMode.Decompress);

        // Qt qCompress prefixes the zlib stream with a big-endian uncompressed length.
        if (extension == ".mmpz" && bytes.Length >= 6 && (bytes[4] & 0x0f) == 8)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(bytes) > 32 * MiB)
                throw new SequencerDocumentException();
            input.Position = 4;
            return new ZLibStream(input, CompressionMode.Decompress);
        }

        return null;
    }

    internal static XElement ReadXml(Stream source, int limit, CancellationToken token)
    {
        using var bounded = new MemoryStream();
        var buffer = new byte[32768];
        int count;
        while ((count = source.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (bounded.Length + count > limit)
                throw new SequencerDocumentException();
            bounded.Write(buffer, 0, count);
        }

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = limit
        };
        bounded.Position = 0;
        using (var check = XmlReader.Create(bounded, settings))
        {
            var nodes = 0;
            while (check.Read())
            {
                token.ThrowIfCancellationRequested();
                if (check.Depth > 64 || ++nodes > 500_000)
                    throw new SequencerDocumentException();
            }
        }

        bounded.Position = 0;
        using var reader = XmlReader.Create(bounded, settings);
        return XElement.Load(reader);
    }

    internal static void SetTempo(SequencerFacts facts, string? text, double scale = 1)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var tempo) && tempo > 0)
            facts.Number("Tempo", tempo * scale);
    }

    private static string? Attribute(XElement? element, params string[] keys) =>
        keys.Select(key => (string?)element?.Attribute(key)).FirstOrDefault(value => value is not null);

    private static SequencerFacts? ReadReaper(byte[] bytes, CancellationToken token)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        do
        {
            line = reader.ReadLine();
            token.ThrowIfCancellationRequested();
        } while (line is not null && string.IsNullOrWhiteSpace(line));
        var header = Tokens(line ?? "").Take(3).ToArray();
        if (header.Length == 0 || header[0] != "<REAPER_PROJECT")
            return null;

        var facts = new SequencerFacts("Project", "rpp");
        facts.Text("Application", "REAPER");
        facts.Text("ApplicationVersion", header.ElementAtOrDefault(2));
        var chunks = new Stack<string>();
        chunks.Push(header[0]);
        var tracks = 0;
        var items = 0;
        var markers = 0;
        string? trackName = null;
        while (chunks.Count > 0 && (line = reader.ReadLine()) is not null)
        {
            token.ThrowIfCancellationRequested();
            var tokens = Tokens(line).Take(3).ToArray();
            if (tokens.Length == 0)
                continue;
            var value = tokens.ElementAtOrDefault(1);
            switch (tokens[0])
            {
                case "<TRACK" when chunks.Count == 1:
                    tracks++;
                    break;
                case "<ITEM" when chunks.Peek() == "<TRACK":
                    items++;
                    break;
                case "NAME" when chunks.Peek() == "<TRACK":
                    trackName = value;
                    break;
                case "TEMPO" when chunks.Count == 1:
                    SetTempo(facts, value);
                    break;
                case "SAMPLERATE" when chunks.Count == 1 && int.TryParse(value, out var rate) && rate > 0:
                    facts.Integer("SampleRate", rate);
                    break;
                case "MARKER" when chunks.Count == 1:
                    markers++;
                    break;
                case ">":
                    if (chunks.Pop() == "<TRACK")
                    {
                        facts.AddNames([trackName], 24 - facts.Names.Count);
                        trackName = null;
                    }
                    break;
            }

            if (tokens[0].StartsWith('<'))
            {
                if (chunks.Count >= 64)
                    throw new SequencerDocumentException();
                chunks.Push(tokens[0]);
            }
        }

        if (chunks.Count != 0)
            facts.Boolean("Truncated", true);
        facts.Integer("TrackCount", tracks);
        facts.Integer("ClipCount", items);
        facts.Integer("MarkerCount", markers);
        return facts;
    }

    private static IEnumerable<string> Tokens(string line)
    {
        var position = 0;
        while (position < line.Length)
        {
            if (char.IsWhiteSpace(line[position]))
            {
                position++;
                continue;
            }

            var quote = line[position] is '"' or '\'' or '`' ? line[position++] : '\0';
            var start = position;
            while (position < line.Length && (quote == '\0' ? !char.IsWhiteSpace(line[position]) : line[position] != quote))
                position++;
            yield return line[start..position];
            if (quote != '\0' && position < line.Length)
                position++;
        }
    }

    private static SequencerFacts? ReadFlp(ReadOnlySpan<byte> data, CancellationToken token)
    {
        if (data.Length < 22 || !data.StartsWith("FLhd"u8) || !data[14..].StartsWith("FLdt"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 6 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) > 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[12..]) == 0)
            return null;

        var facts = new SequencerFacts("Project", "flp");
        facts.Text("Application", "FL Studio");
        facts.Integer("ChannelCount", BinaryPrimitives.ReadUInt16LittleEndian(data[10..]));
        facts.Integer("Division", BinaryPrimitives.ReadUInt16LittleEndian(data[12..]));
        var claimed = BinaryPrimitives.ReadUInt32LittleEndian(data[18..]);
        var events = data.Slice(22, (int)Math.Min(claimed, data.Length - 22L));
        var truncated = claimed > events.Length;
        var patterns = new HashSet<ushort>();
        var media = 0;
        var eventCount = 0;
        var coarseTempo = 0;
        var fineTempo = 0;
        var modernTempo = 0u;
        while (!events.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            var id = events[0];
            events = events[1..];
            var size = id < 64 ? 1 : id < 128 ? 2 : 4;
            if (++eventCount > 250_000 || (id >= 192 && !TryReadLength(ref events, out size)) || size > events.Length)
            {
                truncated = true;
                break;
            }

            var payload = events[..size];
            events = events[size..];
            switch (id)
            {
                case 65:
                    patterns.Add(BinaryPrimitives.ReadUInt16LittleEndian(payload));
                    break;
                case 66:
                    coarseTempo = BinaryPrimitives.ReadUInt16LittleEndian(payload);
                    break;
                case 93:
                    fineTempo = BinaryPrimitives.ReadUInt16LittleEndian(payload);
                    break;
                case 156:
                    modernTempo = BinaryPrimitives.ReadUInt32LittleEndian(payload);
                    break;
                case 194:
                    facts.Text("Title", DecodeFlText(payload));
                    break;
                case 196 when size > 1:
                    media++;
                    break;
                case 199:
                    facts.Text("ApplicationVersion", Encoding.ASCII.GetString(payload[..Math.Min(size, 120)]));
                    break;
            }
        }

        facts.Integer("PatternCount", patterns.Count);
        facts.Integer("MediaFileCount", media);
        if (modernTempo > 0 || coarseTempo > 0)
            facts.Number("Tempo", modernTempo > 0 ? modernTempo / 1000d : coarseTempo + fineTempo / 1000d);
        if (truncated)
            facts.Boolean("Truncated", true);
        return facts;
    }

    private static string DecodeFlText(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 2 && bytes[^2] == 0 && bytes[^1] == 0
            ? Encoding.Unicode.GetString(bytes[..Math.Min(bytes.Length & ~1, 240)])
            : Encoding.Latin1.GetString(bytes[..Math.Min(bytes.Length, 120)]);

    private static bool TryReadLength(ref ReadOnlySpan<byte> bytes, out int length)
    {
        length = 0;
        for (var shift = 0; shift <= 28 && !bytes.IsEmpty; shift += 7)
        {
            var value = bytes[0];
            bytes = bytes[1..];
            if (shift == 28 && value > 7)
                return false;
            length |= (value & 127) << shift;
            if ((value & 128) == 0)
                return true;
        }

        return false;
    }
}
