using System.Buffers.Binary;
using System.Text;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class FlStudioReader
{
    internal static SequencerFacts? Read(ReadOnlySpan<byte> data, CancellationToken token)
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
