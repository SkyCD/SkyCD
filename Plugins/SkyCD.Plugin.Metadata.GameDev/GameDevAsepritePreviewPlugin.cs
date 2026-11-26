using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.Plugin.Metadata.GameDev.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev;

/// <summary>
/// Publishes frame zero of an Aseprite document as the list thumbnail. Visible layers are flattened, so a
/// sprite that draws its artwork on named layers still shows that artwork; transparent pixels keep a PNG,
/// while an opaque first frame is stored as JPEG.
/// </summary>
public sealed class GameDevAsepritePreviewPlugin : IMediaPreviewCapability
{
    private const int PreviewPriority = 20;

    public MediaDescriptor Descriptor =>
        new(GameDevSchema.AsepriteExtensions, GameDevSchema.ContentTypes, GameDevSchema.Preview,
            Priority: PreviewPriority, IndexTimePreview: true);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);

        var (data, truncated) = await GameDevSource
            .ReadWholeAsync(source, GameDevLimits.MaxWholeSourceBytes, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.6));

        if (truncated || data.Length == 0)
        {
            return new MediaPreviewResult([]);
        }

        var image = AsepriteReader.TryRenderFrame(data, out var contentType);
        progress?.Report(new MediaProgress(1));

        return image is null || image.Length == 0
            ? new MediaPreviewResult([])
            : new MediaPreviewResult([new MediaPreviewArtifact(contentType, image)]);
    }
}
