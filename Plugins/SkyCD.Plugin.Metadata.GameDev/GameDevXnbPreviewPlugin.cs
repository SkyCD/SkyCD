using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.Plugin.Metadata.GameDev.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev;

/// <summary>
/// Publishes a compiled XNB Texture2D as the list thumbnail. Only uncompressed surfaces in a plain color
/// format are decoded; a compressed (LZ4/DXT) or non-texture asset yields no artifact, and the metadata
/// extraction still reports what the header says.
/// </summary>
public sealed class GameDevXnbPreviewPlugin : IMediaPreviewCapability
{
    private const int PreviewPriority = 20;

    public MediaDescriptor Descriptor =>
        new(GameDevSchema.XnbExtensions, GameDevSchema.ContentTypes, GameDevSchema.Preview,
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

        var image = MonoGameReader.TryRender(data, out var contentType);
        progress?.Report(new MediaProgress(1));

        return image is null || image.Length == 0
            ? new MediaPreviewResult([])
            : new MediaPreviewResult([new MediaPreviewArtifact(contentType, image)]);
    }
}
