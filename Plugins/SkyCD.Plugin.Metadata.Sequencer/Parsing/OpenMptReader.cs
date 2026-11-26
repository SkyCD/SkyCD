using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class OpenMptReader
{
    internal static bool Matches(ReadOnlySpan<byte> data, string extension) =>
        data.Length >= 192 && (data.StartsWith("tpm."u8) ||
            (data.StartsWith("IMPM"u8) && extension == ".mptm" && ModuleHeader.Word(data, 40) >= 0x0888));

    internal static SequencerFacts? Read(ReadOnlySpan<byte> data) => ImpulseTrackerReader.Read(data, "mptm");
}
