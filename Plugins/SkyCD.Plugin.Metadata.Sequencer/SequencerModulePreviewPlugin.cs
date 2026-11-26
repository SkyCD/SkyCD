using SkyCD.Plugin.Metadata.Sequencer.Models;
using SkyCD.Plugin.Metadata.Sequencer.Sources;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Sequencer;

public sealed class SequencerModulePreviewPlugin : IMediaPreviewCapability
{
    public MediaDescriptor Descriptor => new(SequencerSchema.ModuleExtensions, [],
        SequencerSchema.Preview, Priority: 10);

    public Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        SequencerSource.PreviewAsync(MediaFamily.Module, item, source, progress, cancellationToken);
}
