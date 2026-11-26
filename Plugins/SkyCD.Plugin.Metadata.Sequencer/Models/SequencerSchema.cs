using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Sequencer.Models;

internal static class SequencerSchema
{
    internal static readonly string[] ProjectExtensions =
        [".als", ".flp", ".rpp", ".ardour", ".mmp", ".mmpz", ".rg", ".qtr", ".tracktionedit", ".h2song"];

    internal static readonly string[] MidiExtensions = [".mid", ".midi", ".kar", ".rmi"];
    internal static readonly string[] ModuleExtensions = [".mod", ".nst", ".wow", ".xm", ".s3m", ".it", ".mptm", ".mo3", ".xrns"];

    internal static readonly MediaMetadataSchema Metadata = new("skycd.sequencer.metadata", 1,
    [
        new("Kind", MediaMetadataType.Text),
        new("Format", MediaMetadataType.Text),
        new("Application", MediaMetadataType.Text),
        new("ApplicationVersion", MediaMetadataType.Text),
        new("Title", MediaMetadataType.Text),
        new("Artist", MediaMetadataType.Text),
        new("Copyright", MediaMetadataType.Text),
        new("Genre", MediaMetadataType.Text),
        new("TimeSignature", MediaMetadataType.Text),
        new("KeySignature", MediaMetadataType.Text),
        new("Tracker", MediaMetadataType.Text),
        new("InstrumentNames", MediaMetadataType.Text),
        new("TrackCount", MediaMetadataType.Integer),
        new("DeclaredTrackCount", MediaMetadataType.Integer),
        new("SceneCount", MediaMetadataType.Integer),
        new("ClipCount", MediaMetadataType.Integer),
        new("PatternCount", MediaMetadataType.Integer),
        new("OrderCount", MediaMetadataType.Integer),
        new("InstrumentCount", MediaMetadataType.Integer),
        new("SampleCount", MediaMetadataType.Integer),
        new("ChannelCount", MediaMetadataType.Integer),
        new("NoteCount", MediaMetadataType.Integer),
        new("Division", MediaMetadataType.Integer),
        new("MediaFileCount", MediaMetadataType.Integer),
        new("MarkerCount", MediaMetadataType.Integer),
        new("MidiFormat", MediaMetadataType.Integer),
        new("AudioTrackCount", MediaMetadataType.Integer),
        new("MidiTrackCount", MediaMetadataType.Integer),
        new("ReturnTrackCount", MediaMetadataType.Integer),
        new("Speed", MediaMetadataType.Integer),
        new("InstrumentTrackCount", MediaMetadataType.Integer),
        new("SampleRate", MediaMetadataType.Integer, "Hz"),
        new("Tempo", MediaMetadataType.Number, "BPM"),
        new("Duration", MediaMetadataType.Number, "s"),
        new("LyricsPresent", MediaMetadataType.Boolean),
        new("Truncated", MediaMetadataType.Boolean)
    ]);

    internal static readonly MediaMetadataSchema Preview = new("skycd.sequencer.preview", 1, []);
}
