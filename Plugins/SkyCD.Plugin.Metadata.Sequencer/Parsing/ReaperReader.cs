using System.Text;
using SkyCD.Plugin.Metadata.Sequencer.Exceptions;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class ReaperReader
{
    internal static SequencerFacts? Read(byte[] bytes, CancellationToken token)
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
                    ProjectXml.SetTempo(facts, value);
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
}
