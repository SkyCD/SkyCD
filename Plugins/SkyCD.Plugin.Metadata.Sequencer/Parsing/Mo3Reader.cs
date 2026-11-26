using System.Buffers.Binary;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class Mo3Reader
{
    internal static bool Matches(ReadOnlySpan<byte> data) => data.StartsWith("MO3"u8);

    internal static SequencerFacts? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length <= 8 || data[3] > 5 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) is 0 or >= 0x20000000)
            return null;
        if (data[3] == 5 && (data.Length < 13 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) > data.Length - 12))
            return null;
        var mo3 = new SequencerFacts("Module", "mo3");
        mo3.Text("Tracker", "MO3");
        mo3.Text("ApplicationVersion", data[3].ToString(System.Globalization.CultureInfo.InvariantCulture));
        return mo3;
    }
}
