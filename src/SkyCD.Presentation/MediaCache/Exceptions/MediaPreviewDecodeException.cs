using System;

namespace SkyCD.Presentation.MediaCache.Exceptions;

public sealed class MediaPreviewDecodeException : InvalidOperationException
{
    public MediaPreviewDecodeException(string contentType, long lengthBytes, Exception innerException)
        : base($"The cached preview artifact of type '{contentType}' and {lengthBytes} bytes could not be decoded.", innerException)
    {
    }
}
