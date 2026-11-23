using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using SkyCD.Documents;
using SkyCD.UI.Controls.Preview;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Supplies browser thumbnails from stored cache records only. Realized list items ask for a thumbnail, the
/// answer comes from the durable cache through a shared bounded decode cache, and an entry without a stored
/// preview simply keeps its type icon. Nothing here opens the original media or runs a plugin decoder.
/// </summary>
public sealed class MediaCacheThumbnailProvider : IPreviewThumbnailProvider
{
    private readonly MediaPreviewResolver resolver;
    private readonly MediaPreviewImageService images;

    public MediaCacheThumbnailProvider(MediaPreviewResolver resolver, MediaPreviewImageService images)
    {
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        this.images = images ?? throw new ArgumentNullException(nameof(images));
    }

    /// <remarks>
    /// Resolving and reading the artifact both run synchronously on the calling thread — the thread that
    /// owns the store — and only the decode is offloaded, so a thumbnail still decoding after a catalog is
    /// closed can never reach a disposed database.
    /// </remarks>
    public Task<IImage?> GetThumbnailAsync(object item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item is not CatalogDocument entry || string.IsNullOrWhiteSpace(entry.Id))
        {
            return Task.FromResult<IImage?>(null);
        }

        // Realizing a row while scrolling is not the user opening the item, so it must not disturb LRU order.
        // The list takes the entry's image artifact even when the pane prefers its text excerpt.
        var snapshot = resolver.ResolveThumbnail(entry);
        if (snapshot is null)
        {
            return Task.FromResult<IImage?>(null);
        }

        return images.GetImageAsync(snapshot, cancellationToken);
    }
}
