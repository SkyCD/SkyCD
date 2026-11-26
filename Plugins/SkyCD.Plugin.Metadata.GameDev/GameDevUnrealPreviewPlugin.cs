using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.Plugin.Metadata.GameDev.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev;

/// <summary>
/// Publishes the thumbnail an Unreal package stores in its summary for the asset browser. The image is handed
/// through unchanged; a package without a thumbnail table, or with an entry that does not validate against the
/// file length, yields no artifact.
/// </summary>
public sealed class GameDevUnrealPreviewPlugin : IMediaPreviewCapability
{
    internal const string PngContentType = "image/png";
    internal const string JpegContentType = "image/jpeg";

    private const int PreviewPriority = 20;

    public MediaDescriptor Descriptor =>
        new(GameDevSchema.UnrealExtensions, GameDevSchema.ContentTypes, GameDevSchema.Preview,
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

        var image = UnrealReader.TryReadThumbnail(data);
        progress?.Report(new MediaProgress(1));

        if (image is null || image.Length == 0)
        {
            return new MediaPreviewResult([]);
        }

        var contentType = GameDevSource.StartsWith(image, 0x89, 0x50, 0x4E, 0x47)
            ? PngContentType
            : JpegContentType;
        return new MediaPreviewResult([new MediaPreviewArtifact(contentType, image)]);
    }
}
