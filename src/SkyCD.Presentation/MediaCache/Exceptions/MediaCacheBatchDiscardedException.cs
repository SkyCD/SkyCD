using System;

namespace SkyCD.Presentation.MediaCache.Exceptions;

public sealed class MediaCacheBatchDiscardedException : InvalidOperationException
{
    public MediaCacheBatchDiscardedException(string batchId)
        : base($"The media cache batch '{batchId}' was discarded.")
    {
    }
}
