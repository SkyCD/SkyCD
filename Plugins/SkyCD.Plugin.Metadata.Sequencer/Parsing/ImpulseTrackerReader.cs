using System.Buffers.Binary;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class ImpulseTrackerReader
{
    internal static bool Matches(ReadOnlySpan<byte> data) => data.Length >= 192 && data.StartsWith("IMPM"u8);

    internal static SequencerFacts? Read(ReadOnlySpan<byte> data, string format)
    {
        var orders = ModuleHeader.Word(data, 32);
        var instruments = ModuleHeader.Word(data, 34);
        var samples = ModuleHeader.Word(data, 36);
        var patterns = ModuleHeader.Word(data, 38);
        if (192 + orders + 4 * (instruments + samples + patterns) > data.Length)
            return null;

        var channels = data.Slice(64, 64).ToArray().Count(pan => pan < 128);
        var facts = ModuleHeader.Header(format, data.Slice(4, 26), format == "mptm" ? "OpenMPT" : $"Impulse Tracker (0x{ModuleHeader.Word(data, 40):X4})",
            channels, orders, patterns, instruments, samples, data[51], data[50]);
        for (var i = 0; i < instruments + samples; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(data[(192 + orders + i * 4)..]);
            if (offset == 0)
                continue;
            if (offset > data.Length - 58)
                return null;
            var header = data[(int)offset..];
            if (!header.StartsWith(i < instruments ? "IMPI"u8 : "IMPS"u8))
                return null;
            if (facts.Names.Count < 32)
                facts.AddNames([ModuleHeader.Decode(header.Slice(32 - (i < instruments ? 0 : 12), 26))], 1);
        }
        return facts;
    }
}
