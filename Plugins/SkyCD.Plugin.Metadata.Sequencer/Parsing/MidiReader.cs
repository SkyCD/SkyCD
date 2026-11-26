using System.Buffers.Binary;
using System.Globalization;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class MidiReader
{
    internal static SequencerFacts? Read(byte[] bytes, string extension, CancellationToken token)
    {
        var (start, length) = FindSmf(bytes);
        var data = bytes.AsSpan(start, length);
        if (data.Length < 14 || !data.StartsWith("MThd"u8) || BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != 6)
            return null;

        var format = BinaryPrimitives.ReadUInt16BigEndian(data[8..]);
        var division = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
        if (format > 2 || division == 0)
            return null;

        var facts = new SequencerFacts("Midi", "smf");
        facts.Integer("MidiFormat", format);
        facts.Integer("DeclaredTrackCount", BinaryPrimitives.ReadUInt16BigEndian(data[10..]));
        facts.Integer("Division", division);
        var trackCount = CountTracks(data[14..], token);
        if (trackCount == 0)
            return null;
        facts.Integer("TrackCount", trackCount);

        using var stream = new MemoryStream(bytes, start, length);
        using var reader = MidiFile.ReadLazy(stream, new ReadingSettings
        {
            EndOfTrackStoringPolicy = EndOfTrackStoringPolicy.Store
        });
        var tracks = new List<TrackChunk>();
        TrackChunk? current = null;
        TimeDivision? timeDivision = null;
        var events = 0;
        var truncated = false;
        var lyrics = false;
        var instruments = new HashSet<string>(StringComparer.Ordinal);
        string? title = null;
        string? copyright = null;
        while (reader.ReadToken() is { } entry)
        {
            token.ThrowIfCancellationRequested();
            switch (entry)
            {
                case FileHeaderToken header:
                    timeDivision = header.TimeDivision;
                    break;
                case ChunkHeaderToken chunk:
                    current = chunk.ChunkId == TrackChunk.Id ? new TrackChunk() : null;
                    if (current is not null)
                        tracks.Add(current);
                    break;
                case MidiEventToken midi when current is not null:
                    if (++events > 250_000)
                    {
                        truncated = true;
                        break;
                    }

                    current.Events.Add(midi.Event);
                    switch (midi.Event)
                    {
                        case SequenceTrackNameEvent name:
                            title ??= name.Text;
                            break;
                        case CopyrightNoticeEvent notice:
                            copyright ??= notice.Text;
                            break;
                        case InstrumentNameEvent instrument when instruments.Count < 32:
                            instruments.Add(instrument.Text);
                            break;
                        case LyricEvent:
                            lyrics = true;
                            break;
                        case TextEvent text when text.Text.StartsWith('/') || text.Text.StartsWith('\\'):
                            lyrics = true;
                            break;
                    }
                    break;
            }

            if (truncated)
                break;
        }

        if (timeDivision is null || tracks.Count == 0)
            return null;

        var file = new MidiFile(tracks) { TimeDivision = timeDivision };
        var tempoMap = (format == 2 ? new MidiFile(tracks[0]) { TimeDivision = timeDivision } : file).GetTempoMap();
        var time = tempoMap.GetTimeSignatureAtTime(new MidiTimeSpan(0));
        facts.Number("Tempo", tempoMap.GetTempoAtTime(new MidiTimeSpan(0)).BeatsPerMinute);
        facts.Text("TimeSignature", FormattableString.Invariant($"{time.Numerator}/{time.Denominator}"));
        var key = tracks[0].Events.TakeWhile(e => e.DeltaTime == 0).OfType<KeySignatureEvent>().LastOrDefault();
        if (key is not null && key.Key is >= -7 and <= 7)
        {
            string[] major = ["Cb", "Gb", "Db", "Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#"];
            string[] minor = ["Ab", "Eb", "Bb", "F", "C", "G", "D", "A", "E", "B", "F#", "C#", "G#", "D#", "A#"];
            facts.Text("KeySignature", key.Scale == 0 ? major[key.Key + 7] + " major" : minor[key.Key + 7] + " minor");
        }
        facts.Text("Title", title);
        facts.Text("Copyright", copyright);
        facts.Text("InstrumentNames", string.Join(", ", instruments.Select(name => name[..Math.Min(name.Length, 120)])));
        if (extension == ".kar")
            facts.Boolean("LyricsPresent", lyrics);

        if (truncated)
        {
            facts.Boolean("Truncated", true);
            return facts;
        }

        var count = 0;
        var duration = 0d;
        foreach (var track in tracks)
        {
            token.ThrowIfCancellationRequested();
            var map = format == 2 ? new MidiFile(track) { TimeDivision = timeDivision }.GetTempoMap() : tempoMap;
            duration = Math.Max(duration, ToSeconds(track.Events.Sum(e => e.DeltaTime), map));
            foreach (var _ in track.GetNotes())
            {
                token.ThrowIfCancellationRequested();
                count++;
            }
        }

        facts.Integer("NoteCount", count);
        facts.Number("Duration", duration);
        return facts;
    }

    private static double ToSeconds(long tick, TempoMap map) =>
        TimeConverter.ConvertTo<MetricTimeSpan>(tick, map).TotalMicroseconds / 1_000_000d;

    private static int CountTracks(ReadOnlySpan<byte> bytes, CancellationToken token)
    {
        var tracks = 0;
        while (bytes.Length >= 8)
        {
            token.ThrowIfCancellationRequested();
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[4..]);
            if (length > bytes.Length - 8)
                return 0;
            if (bytes.StartsWith("MTrk"u8))
                tracks++;
            bytes = bytes[(8 + (int)length)..];
        }
        return bytes.IsEmpty ? tracks : 0;
    }

    private static (int Start, int Length) FindSmf(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("MThd"u8))
            return (0, bytes.Length);

        if (bytes.Length < 12 || !bytes.StartsWith("RIFF"u8) || !bytes[8..].StartsWith("RMID"u8))
            return (0, 0);

        var end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (end > bytes.Length)
            return (0, 0);

        for (var position = 12; position + 8 <= end;)
        {
            var chunk = bytes[position..];
            var length = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            if (position + 8L + length + (length & 1) > end)
                break;
            if (chunk.StartsWith("data"u8))
                return (position + 8, (int)length);
            position += 8 + (int)length + (int)(length & 1);
        }
        return (0, 0);
    }
}
