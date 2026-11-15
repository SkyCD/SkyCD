using System;

namespace SkyCD.Presentation.MediaCache.Exceptions;

public sealed class MediaCacheBundleManifestNotFoundException : InvalidOperationException
{
    public MediaCacheBundleManifestNotFoundException(string manifestPath)
        : base($"The media cache bundle manifest '{manifestPath}' was not found.")
    {
    }
}
