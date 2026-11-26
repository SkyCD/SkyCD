using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.Plugin.Metadata.GameDev.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev;

/// <summary>
/// Publishes the JPEG thumbnail a Wicked Engine scene embeds after its header. Only the header and the declared
/// thumbnail bytes are read, so a large scene never has to fit in the read budget.
/// </summary>
public sealed class GameDevWickedScenePreviewPlugin : IMediaPreviewCapability
{
    internal const string JpegContentType = "image/jpeg";

    private const int PreviewPriority = 20;

    public MediaDescriptor Descriptor =>
        new(GameDevSchema.WickedExtensions, GameDevSchema.ContentTypes, GameDevSchema.Preview,
            Priority: PreviewPriority, IndexTimePreview: true);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);

        var (data, _) = await GameDevSource
            .ReadPrefixAsync(source, GameDevLimits.MaxArtifactBytes + 16, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.6));

        var image = WickedReader.TryReadThumbnail(data);
        progress?.Report(new MediaProgress(1));

        return image is null || image.Length == 0
            ? new MediaPreviewResult([])
            : new MediaPreviewResult([new MediaPreviewArtifact(JpegContentType, image)]);
    }
}
