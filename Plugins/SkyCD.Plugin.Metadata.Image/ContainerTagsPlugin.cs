using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.MediaMetadata.Models;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.Plugin.Metadata.Image.Parsing;

namespace SkyCD.Plugin.Metadata.Image;

/// <summary>
/// Publishes the keywords, comment and rating stored inside TIFF containers and the TIFF-based RAW formats.
/// The thumbnail decoder does not present these containers, so only a bounded header prefix is read: the
/// carriers sit in the first IFD and early regions, which keeps a scan cheap on multi-gigabyte RAW files.
/// </summary>
public sealed class ContainerTagsPlugin : IMediaMetadataCapability
{
    private const int ProviderPriority = 10;

    /// <summary>Enough for the first IFD, its out-of-line values and an embedded XMP packet.</summary>
    private const int MaxScanBytes = 1024 * 1024;

    public MediaDescriptor Descriptor => new(ImageSchema.ContainerExtensions,
        ImageSchema.ContainerContentTypes, ImageSchema.ContainerTags, Priority: ProviderPriority);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var data = await SourcePrefix.ReadAsync(source, item.Length, MaxScanBytes, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.6));

        // A damaged container must never fail the item; the reader yields an empty scan instead, and the
        // catalog keeps the file without tags.
        var embedded = EmbeddedMetadataReader.Read(data);

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(
            MetadataValues.Build(embedded.Tags, ImageSchema.Tags, ImageSchema.Comment, ImageSchema.Rating));
    }
}
