using SkyCD.Plugin.Metadata.Models.Export;
using SkyCD.Plugin.Metadata.Models.Models;
using SkyCD.Plugin.Metadata.Models.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Models;

public sealed class ModelPreviewPlugin : IMediaPreviewCapability
{
    public MediaDescriptor Descriptor => ModelSchema.Preview;

    public async Task<MediaPreviewResult> GeneratePreviewAsync(MediaItem item, IMediaSource source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        var result = await ModelReader.ReadAsync(item, source, cancellationToken).ConfigureAwait(false);
        var artifact = ModelThumbnail.Render(result.Triangles, cancellationToken);
        progress?.Report(new MediaProgress(1));
        return new(artifact is null ? [] : [artifact]);
    }
}
