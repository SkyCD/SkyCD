using SkyCD.Plugin.Metadata.Sequencer.Models;
using SkyCD.Plugin.Metadata.Sequencer.Sources;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Sequencer;

public sealed class SequencerProjectMetadataPlugin : IMediaMetadataCapability
{
    public MediaDescriptor Descriptor => new(SequencerSchema.ProjectExtensions, [],
        SequencerSchema.Metadata, Priority: 10);

    public Task<MediaExtractionResult> ExtractAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        SequencerSource.ExtractAsync(MediaFamily.Project, item, source, progress, cancellationToken);
}
