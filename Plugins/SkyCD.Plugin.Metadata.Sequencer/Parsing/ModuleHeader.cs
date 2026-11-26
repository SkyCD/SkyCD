using System.Buffers.Binary;
using System.Text;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class ModuleHeader
{
    internal static ushort Word(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    internal static string Decode(ReadOnlySpan<byte> data) => Encoding.Latin1.GetString(data).Split('\0', 2)[0];

    internal static SequencerFacts Header(string format, ReadOnlySpan<byte> title, string tracker, int channels,
        int orders, int patterns, int? instruments, int? samples, int tempo, int speed)
    {
        var facts = new SequencerFacts("Module", format);
        facts.Text("Title", Decode(title));
        facts.Text("Tracker", tracker);
        facts.Integer("ChannelCount", channels);
        facts.Integer("OrderCount", orders);
        facts.Integer("PatternCount", patterns);
        if (instruments.HasValue)
            facts.Integer("InstrumentCount", instruments.Value);
        if (samples.HasValue)
            facts.Integer("SampleCount", samples.Value);
        if (tempo > 0)
            facts.Number("Tempo", tempo);
        if (speed > 0)
            facts.Integer("Speed", speed);
        return facts;
    }
}
