using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Serves decoded preview images from the durable cache and keeps them in a bounded decode cache. One
/// instance serves both the details pane and the browser thumbnails, so an artifact already on screen is
/// never decoded twice and the decoded working set cannot grow without limit.
/// </summary>
public sealed class MediaPreviewImageService : IDisposable
{
    private readonly MediaPreviewResolver resolver;
    private readonly IPreviewImageDecoder decoder;
    private readonly BoundedAsyncCache<IImage> images;
    private bool isDisposed;

    public MediaPreviewImageService(
        MediaPreviewResolver resolver,
        MediaPreviewDisplayPolicy? policy = null,
        IPreviewImageDecoder? decoder = null)
    {
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.decoder = decoder ?? new AvaloniaPreviewImageDecoder();
        Policy = policy ?? resolver.Policy;
        images = new BoundedAsyncCache<IImage>(Policy.ImageCacheEntries, Policy.ImageCacheBytes, Measure);
    }

    public MediaPreviewDisplayPolicy Policy { get; }

    /// <summary>Decoded images currently held.</summary>
    public int CachedImageCount => images.Count;

    /// <summary>Releases every decoded image; used after a refresh may have replaced stored artifacts.</summary>
    public void ClearCache() => images.Clear();

    /// <summary>
    /// Returns the decoded image of a resolved snapshot, or null when the snapshot carries no image payload
    /// or its bytes are unavailable. Decode failures surface as
    /// <see cref="Exceptions.MediaPreviewDecodeException"/> so the caller can show a failed state instead of
    /// an empty pane.
    /// </summary>
    public Task<IImage?> GetImageAsync(EntryPreviewSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (snapshot.PayloadKind != PreviewPayloadKind.Image ||
            snapshot.RecordId is null ||
            snapshot.Artifact is null)
        {
            return Task.FromResult<IImage?>(null);
        }

        var contentType = snapshot.Artifact.ContentType;
        var declaredLength = snapshot.Artifact.Length;

        return images.GetAsync(
            CreateCacheKey(snapshot.RecordId, snapshot.Artifact.Key),
            async token =>
            {
                if (!resolver.TryReadPayload(snapshot, out var data) || data.Length == 0)
                {
                    return null;
                }

                return await decoder.DecodeAsync(data, contentType, declaredLength, token).ConfigureAwait(false);
            },
            cancellationToken);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        images.Dispose();
    }

    /// <summary>Stable identity of a decoded image; records are immutable once captured.</summary>
    public static string CreateCacheKey(string recordId, string artifactKey) => recordId + '|' + artifactKey;

    private static long Measure(IImage image) => image is Avalonia.Media.Imaging.Bitmap bitmap
        ? MediaPreviewDisplayPolicy.EstimateDecodedImageBytes(bitmap.PixelSize.Width, bitmap.PixelSize.Height)
        : 0;
}
