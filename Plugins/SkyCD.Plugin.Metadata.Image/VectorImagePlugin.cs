using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.Plugin.Metadata.Image.Parsing;
using SkyCD.Plugin.Metadata.Image.Parsing.Vector;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Image;

public sealed class VectorImagePlugin : IMediaMetadataCapability
{
    public MediaDescriptor Descriptor => new(Extensions, [], ContainerSchema.Document);

    internal static readonly string[] Extensions = [".svg", ".svgz", ".ai", ".eps", ".epsf", ".epsi", ".wmf", ".emf"];

    public Task<MediaExtractionResult> ExtractAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        ContainerReader.ExtractAsync(item, source, VectorImageReader.ReadMetadata, progress, cancellationToken);
}
