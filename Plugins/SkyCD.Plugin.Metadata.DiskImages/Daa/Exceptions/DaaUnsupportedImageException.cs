using System;

namespace SkyCD.Plugin.Metadata.DiskImages.Daa.Exceptions;

public sealed class DaaUnsupportedImageException : Exception
{
    public DaaUnsupportedImageException(string feature)
        : base($"This DAA image uses an unsupported feature: {feature}.")
    {
    }
}
