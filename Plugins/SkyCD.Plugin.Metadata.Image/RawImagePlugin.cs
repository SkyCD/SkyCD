using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.Plugin.Metadata.Image.Parsing;
using SkyCD.Plugin.Metadata.Image.Parsing.Raw;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Image;

public sealed class RawImagePlugin : IMediaMetadataCapability
{
    public MediaDescriptor Descriptor => new(Extensions, [], ContainerSchema.Raw);

    internal static readonly string[] Extensions = [".3fr", ".arw", ".cr2", ".crw", ".crx", ".dng", ".gpr", ".kdc", ".nef", ".orf", ".pef", ".raf", ".rw2", ".rwl", ".srw"];

    public Task<MediaExtractionResult> ExtractAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken) =>
        ContainerReader.ExtractAsync(item, source, RawImageReader.ReadMetadata, progress, cancellationToken);
}
