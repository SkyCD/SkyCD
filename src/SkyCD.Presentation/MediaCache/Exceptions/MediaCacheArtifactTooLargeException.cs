using System;

namespace SkyCD.Presentation.MediaCache.Exceptions;

public sealed class MediaCacheArtifactTooLargeException : InvalidOperationException
{
    public MediaCacheArtifactTooLargeException(string recordId, long artifactBytes, long limitBytes)
        : base($"The media cache record '{recordId}' declares {artifactBytes} artifact bytes, which exceeds the {limitBytes} byte per-record limit.")
    {
    }
}
