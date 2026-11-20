using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.MediaMetadata;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Publishes the keywords, comment and rating that travel inside the carriers the image plugin does not
/// present: TIFF containers, the TIFF-based RAW formats and standalone XMP sidecars. Sources without any of
/// these tags yield an empty result instead of a failure, and damaged containers never fail the item.
/// </summary>
/// <remarks>
/// Metadata the user assigned through the operating system is not part of the file bytes; it is read by
/// <see cref="OsMetadataPlugin"/>, which is addressed by path. See <c>docs/plugins/file-metadata.md</c>.
/// </remarks>
public sealed class FileMetadataPlugin : IMediaMetadataCapability
{
    private const int ProviderPriority = 10;
    private const int ChunkBytes = 256 * 1024;

    /// <summary>
    /// Only a bounded prefix is read, because these tags sit in header directories and early file regions;
    /// the cap keeps a scan cheap even on multi-gigabyte RAW files.
    /// </summary>
    private const int MaxScanBytes = 1024 * 1024;

    public MediaDescriptor Descriptor => new(FileMetadataSchema.EmbeddedExtensions,
        FileMetadataSchema.EmbeddedContentTypes, FileMetadataSchema.Embedded, Priority: ProviderPriority);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var length = source.Length > 0 ? source.Length : item.Length;
        if (length <= 0)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult([]);
        }

        var data = await ReadPrefixAsync(source, (int)Math.Min(length, MaxScanBytes), cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.6));

        MetadataScan scan;
        try
        {
            scan = FileMetadataScanner.Scan(data);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A damaged container must never fail the whole item; the catalog keeps the file without tags.
            scan = new MetadataScan();
        }

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(MetadataValues.Build(scan, FileMetadataSchema.Tags,
            FileMetadataSchema.Comment, FileMetadataSchema.Rating));
    }

    /// <summary>Reads at most <paramref name="length"/> bytes in bounded chunks, honoring cancellation.</summary>
    private static async Task<byte[]> ReadPrefixAsync(IMediaSource source, int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = Math.Min(ChunkBytes, length - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            chunk.Span.CopyTo(buffer.AsSpan(offset));
            offset += chunk.Length;
            if (chunk.Length != count)
            {
                // A short read means the source is smaller than advertised; scan what was actually read.
                Array.Resize(ref buffer, offset);
                break;
            }
        }

        return buffer;
    }
}
