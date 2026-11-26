using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class ProjectReader
{
    internal static SequencerFacts? Read(byte[] bytes, string extension, CancellationToken token) => extension switch
    {
        ".als" => AbletonReader.Read(bytes, token),
        ".ardour" => ArdourReader.Read(bytes, token),
        ".mmp" => LmmsReader.Read(bytes, token, compressed: false),
        ".rg" => RosegardenReader.Read(bytes, token),
        ".qtr" => QtractorReader.Read(bytes, token),
        ".tracktionedit" => TracktionReader.Read(bytes, token),
        ".h2song" => HydrogenReader.Read(bytes, token),
        ".mmpz" => LmmsReader.Read(bytes, token, compressed: true),
        ".flp" => FlStudioReader.Read(bytes, token),
        ".rpp" => ReaperReader.Read(bytes, token),
        _ => null
    };
}
