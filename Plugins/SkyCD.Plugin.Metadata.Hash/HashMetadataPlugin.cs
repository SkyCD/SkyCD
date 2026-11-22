using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Hash.Models;
using SkyCD.Plugin.Metadata.Hash.Parsing;

namespace SkyCD.Plugin.Metadata.Hash;

public sealed class HashMetadataPlugin : IMediaMetadataCapability
{
    public MediaDescriptor Descriptor => new(
        HashSchema.Extensions,
        HashSchema.ContentTypes,
        HashSchema.Metadata,
        Priority: 0,
        IsOptIn: true);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);

        var hashes = await HashCalculator.ComputeAsync(source, item.Length, progress, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(1));

        var values = new List<MediaMetadataValue>(3)
        {
            new(HashSchema.Ed2k, MediaMetadataType.Text, Text: hashes.Ed2k),
            new(HashSchema.Tth, MediaMetadataType.Text, Text: hashes.Tth),
            new(HashSchema.Sha256, MediaMetadataType.Text, Text: hashes.Sha256)
        };
        return new MediaExtractionResult(values);
    }
}
