using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class S3mReader
{
    internal static bool Matches(ReadOnlySpan<byte> data) =>
        data.Length >= 96 && data[28] == 0x1a && data[29] == 16 && data[44..].StartsWith("SCRM"u8);

    internal static SequencerFacts? Read(ReadOnlySpan<byte> data)
    {
        var orders = ModuleHeader.Word(data, 32);
        var instruments = ModuleHeader.Word(data, 34);
        var patterns = ModuleHeader.Word(data, 36);
        if (96 + orders + 2 * (instruments + patterns) > data.Length)
            return null;

        var channels = data.Slice(64, 32).ToArray().Count(setting => setting < 32);
        var samples = 0;
        var names = new List<string>();
        for (var i = 0; i < instruments; i++)
        {
            var pointer = ModuleHeader.Word(data, 96 + orders + i * 2) * 16;
            if (pointer == 0)
                continue;
            if (pointer > data.Length - 80)
                return null;
            if (data[pointer] == 1)
                samples++;
            if (names.Count < 32)
                names.Add(ModuleHeader.Decode(data.Slice(pointer + 48, 28)));
        }

        var facts = ModuleHeader.Header("s3m", data[..28], $"Scream Tracker (0x{ModuleHeader.Word(data, 40):X4})", channels,
            orders, patterns, instruments, samples, data[50], data[49]);
        facts.AddNames(names, 32);
        return facts;
    }
}
