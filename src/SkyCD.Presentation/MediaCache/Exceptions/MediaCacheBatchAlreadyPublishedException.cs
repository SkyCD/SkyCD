using System;

namespace SkyCD.Presentation.MediaCache.Exceptions;

public sealed class MediaCacheBatchAlreadyPublishedException : InvalidOperationException
{
    public MediaCacheBatchAlreadyPublishedException(string batchId)
        : base($"The media cache batch '{batchId}' was already published.")
    {
    }
}
