using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.Plugin.Metadata.GameDev.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev;

/// <summary>
/// Publishes the package's own <c>preview.png</c> as the list thumbnail, choosing the largest one when the
/// package carries several assets. The image is passed through unchanged unless it exceeds the thumbnail
/// bound, so no asset inside the package is decoded, imported or written.
/// </summary>
public sealed class GameDevUnityPackagePreviewPlugin : IMediaPreviewCapability
{
    private const int PreviewPriority = 20;

    public MediaDescriptor Descriptor =>
        new(GameDevSchema.UnityPackageExtensions, GameDevSchema.ContentTypes, GameDevSchema.Preview,
            Priority: PreviewPriority, IndexTimePreview: true);

    public Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        var image = UnityPackageReader.TryReadLargestPreview(source, cancellationToken);
        progress?.Report(new MediaProgress(1));

        return Task.FromResult(image is null || image.Length == 0
            ? new MediaPreviewResult([])
            : new MediaPreviewResult([new MediaPreviewArtifact(ContentType(image), image)]));
    }

    private static string ContentType(ReadOnlySpan<byte> image)
    {
        if (GameDevSource.StartsWith(image, 0x89, 0x50, 0x4E, 0x47))
        {
            return "image/png";
        }

        return GameDevSource.StartsWith(image, 0xFF, 0xD8) ? "image/jpeg" : "image/png";
    }
}
