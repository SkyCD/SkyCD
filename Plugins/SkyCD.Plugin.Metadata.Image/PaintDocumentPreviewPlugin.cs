using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.Plugin.Metadata.Image.Parsing;
using SkyCD.Plugin.Metadata.Image.Parsing.Paint;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Image;

public sealed class PaintDocumentPreviewPlugin : IMediaPreviewCapability
{
    public PaintDocumentPreviewPlugin() => NativeImageReader.EnsureInitialized();

    public MediaDescriptor Descriptor => new(PaintDocumentPlugin.Extensions, [], ContainerSchema.Preview, IndexTimePreview: true);

    public Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        ContainerReader.PreviewAsync(item, source, PaintDocumentReader.ReadPreview, progress, cancellationToken);
}
