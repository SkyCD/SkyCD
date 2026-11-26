using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.Plugin.Metadata.Image.Parsing;
using SkyCD.Plugin.Metadata.Image.Parsing.Paint;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Image;

public sealed class PaintDocumentPlugin : IMediaMetadataCapability
{
    public MediaDescriptor Descriptor => new(Extensions, [], ContainerSchema.Document);

    internal static readonly string[] Extensions = [".xcf", ".kra", ".ora", ".clip", ".afphoto", ".afdesign", ".afpub", ".aftemplate"];

    public Task<MediaExtractionResult> ExtractAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        ContainerReader.ExtractAsync(item, source, PaintDocumentReader.ReadMetadata, progress, cancellationToken);
}
