using System;

namespace SkyCD.Presentation.MediaCache.Exceptions;

public sealed class MediaCacheBundleFormatException : InvalidOperationException
{
    public MediaCacheBundleFormatException(int foundVersion, int supportedVersion)
        : base($"The media cache bundle uses format version {foundVersion}, but only versions up to {supportedVersion} are supported.")
    {
    }
}
