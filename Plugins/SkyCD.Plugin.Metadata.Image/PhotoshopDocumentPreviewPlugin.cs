using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.Plugin.Metadata.Image.Parsing;
using SkyCD.Plugin.Metadata.Image.Parsing.Photoshop;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Image;

public sealed class PhotoshopDocumentPreviewPlugin : IMediaPreviewCapability
{
    public MediaDescriptor Descriptor => new(PhotoshopDocumentPlugin.Extensions, [], ContainerSchema.Preview, IndexTimePreview: true);

    public Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        ContainerReader.PreviewAsync(item, source, PhotoshopDocumentReader.ReadPreview, progress, cancellationToken);
}
