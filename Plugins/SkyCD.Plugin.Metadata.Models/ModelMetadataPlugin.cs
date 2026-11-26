using SkyCD.Plugin.Metadata.Models.Models;
using SkyCD.Plugin.Metadata.Models.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Models;

public sealed class ModelMetadataPlugin : IMediaMetadataCapability
{
    public MediaDescriptor Descriptor => ModelSchema.Metadata;

    public async Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        var result = await ModelReader.ReadAsync(item, source, cancellationToken).ConfigureAwait(false);
        progress?.Report(new MediaProgress(1));
        return new(result.Values);
    }
}
