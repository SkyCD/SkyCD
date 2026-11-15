using System;

namespace SkyCD.Presentation.MediaCache.Exceptions;

public sealed class MediaCacheBatchNotFoundException : InvalidOperationException
{
    public MediaCacheBatchNotFoundException(string batchId)
        : base($"The media cache batch '{batchId}' was not found.")
    {
    }
}
