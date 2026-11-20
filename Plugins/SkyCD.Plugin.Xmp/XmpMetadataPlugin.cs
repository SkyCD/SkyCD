using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.MediaMetadata;

namespace SkyCD.Plugin.Xmp;

/// <summary>
/// Publishes the keywords, description and rating stored in a standalone XMP sidecar. The packet is read
/// from a bounded prefix; malformed XML yields no values instead of failing the item. XMP embedded inside an
/// image or TIFF is read by the plugin that claims that container, so a sidecar is the only source claimed
/// here.
/// </summary>
public sealed class XmpMetadataPlugin : IMediaMetadataCapability
{
    private const int ProviderPriority = 10;

    /// <summary>Sidecars are text; the packet itself is expected well inside this bound.</summary>
    private const int MaxScanBytes = 1024 * 1024;

    public MediaDescriptor Descriptor =>
        new(XmpSchema.Extensions, XmpSchema.ContentTypes, XmpSchema.Schema, Priority: ProviderPriority);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var data = await SourcePrefix.ReadAsync(source, item.Length, MaxScanBytes, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.6));

        var scan = new MetadataScan();
        try
        {
            XmpPacketReader.Read(data, scan);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A damaged packet must never fail the item; the catalog keeps the sidecar without values.
        }

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(
            MetadataValues.Build(scan, XmpSchema.Tags, XmpSchema.Comment, XmpSchema.Rating));
    }
}
