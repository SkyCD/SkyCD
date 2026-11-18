using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Hash;

/// <summary>
/// Computes interoperable P2P file identifiers for any source. The provider is opt-in: the host runs it
/// only for an explicit compute request, so opening a preview or scanning never starts a full-file read
/// unless the user asked for it.
/// </summary>
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
