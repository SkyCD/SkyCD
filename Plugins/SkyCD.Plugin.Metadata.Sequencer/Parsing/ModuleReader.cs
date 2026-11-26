using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class ModuleReader
{
    internal static SequencerFacts? Read(byte[] bytes, string extension, CancellationToken token)
    {
        var data = bytes.AsSpan();
        if (RenoiseReader.Matches(data, extension))
            return RenoiseReader.Read(bytes, token);
        if (XmReader.Matches(data))
            return XmReader.Read(data, token);
        if (S3mReader.Matches(data))
            return S3mReader.Read(data);
        if (OpenMptReader.Matches(data, extension))
            return OpenMptReader.Read(data);
        if (ImpulseTrackerReader.Matches(data))
            return ImpulseTrackerReader.Read(data, "it");
        if (Mo3Reader.Matches(data))
            return Mo3Reader.Read(data);
        return ModReader.Read(data);
    }
}
