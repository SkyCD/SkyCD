using SkyCD.Plugin.Metadata.Sequencer.Models;
using SkyCD.Plugin.Metadata.Sequencer.Sources;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Sequencer;

public sealed class SequencerMidiPreviewPlugin : IMediaPreviewCapability
{
    public MediaDescriptor Descriptor => new(SequencerSchema.MidiExtensions, ["audio/midi", "audio/x-midi"],
        SequencerSchema.Preview, Priority: 10, IndexTimePreview: true);

    public Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        SequencerSource.PreviewAsync(MediaFamily.Midi, item, source, progress, cancellationToken);
}
