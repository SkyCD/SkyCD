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

    public async Task<IImage?> GetThumbnailAsync(object item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item is not CatalogDocument entry || string.IsNullOrWhiteSpace(entry.Id))
        {
            return null;
        }

        var snapshot = await Task.Run(() => resolver.Resolve(entry), cancellationToken).ConfigureAwait(true);
        if (snapshot.PayloadKind != PreviewPayloadKind.Image)
        {
            return null;
        }

        return await images.GetImageAsync(snapshot, cancellationToken).ConfigureAwait(true);
    }
}
