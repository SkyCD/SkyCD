using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.Plugin.Metadata.Image.Parsing;
using SkyCD.Plugin.Metadata.Image.Parsing.Vector;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Image;

public sealed class VectorImagePreviewPlugin : IMediaPreviewCapability
{
    public MediaDescriptor Descriptor => new(VectorImagePlugin.Extensions, [], ContainerSchema.Preview, IndexTimePreview: true);

    public Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        ContainerReader.PreviewAsync(item, source, VectorImageReader.ReadPreview, progress, cancellationToken);
}
