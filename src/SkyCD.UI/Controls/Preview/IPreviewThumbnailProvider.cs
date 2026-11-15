using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;

namespace SkyCD.UI.Controls.Preview;

/// <summary>
/// Supplies cached thumbnails for realized browser items. Implementations must answer from stored data
/// only and must never reconnect media or run a decoder, so browsing stays instant and offline.
/// </summary>
public interface IPreviewThumbnailProvider
{
    /// <summary>
    /// Returns the thumbnail of <paramref name="item"/>, or null when none is cached or the payload
    /// cannot be decoded. Implementations honour <paramref name="cancellationToken"/>, which is signalled
    /// as soon as the item is re-selected, recycled by virtualization or scrolled away.
    /// </summary>
    Task<IImage?> GetThumbnailAsync(object item, CancellationToken cancellationToken);
}
