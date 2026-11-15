using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Turns stored preview bytes into a displayable image. Kept separate from the caching service so the
/// bounds, coalescing and eviction stay testable independently of the platform decoder.
/// </summary>
public interface IPreviewImageDecoder
{
    /// <summary>
    /// Decodes one artifact. Implementations run off the UI thread and report an undecodable payload by
    /// throwing <see cref="Exceptions.MediaPreviewDecodeException"/>.
    /// </summary>
    Task<IImage?> DecodeAsync(
        byte[] data,
        string contentType,
        long declaredLength,
        CancellationToken cancellationToken);
}
