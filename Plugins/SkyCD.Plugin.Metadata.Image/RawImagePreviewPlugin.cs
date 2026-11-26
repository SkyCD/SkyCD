using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.Plugin.Metadata.Image.Parsing;
using SkyCD.Plugin.Metadata.Image.Parsing.Raw;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Image;

public sealed class RawImagePreviewPlugin : IMediaPreviewCapability
{
    public MediaDescriptor Descriptor => new(RawImagePlugin.Extensions, [], ContainerSchema.Preview, IndexTimePreview: true);

    public Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        ContainerReader.PreviewAsync(item, source, RawImageReader.ReadPreview, progress, cancellationToken);
}
