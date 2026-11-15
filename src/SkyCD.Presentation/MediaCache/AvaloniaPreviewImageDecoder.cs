using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkyCD.Presentation.MediaCache.Exceptions;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Platform decoder for stored preview artifacts. Decoding happens on a thread-pool thread so a large or
/// malformed payload can never block navigation, and a payload that produces no pixels is reported as a
/// decode failure rather than shown as a blank preview.
/// </summary>
public sealed class AvaloniaPreviewImageDecoder : IPreviewImageDecoder
{
    public async Task<IImage?> DecodeAsync(
        byte[] data,
        string contentType,
        long declaredLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length == 0)
        {
            return null;
        }

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(data, writable: false);
                var bitmap = new Bitmap(stream);

                // Some backends accept a payload they cannot turn into pixels and report an empty size
                // instead of throwing, so the decoded result is validated before it is shown or cached.
                if (bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0)
                {
                    bitmap.Dispose();
                    throw new InvalidDataException("The decoded preview has no pixels.");
                }

                return bitmap;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MediaPreviewDecodeException(contentType, declaredLength, exception);
        }
    }
}
