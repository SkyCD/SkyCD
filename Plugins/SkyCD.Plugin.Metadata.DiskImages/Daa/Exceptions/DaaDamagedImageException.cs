using System;

namespace SkyCD.Plugin.Metadata.DiskImages.Daa.Exceptions;

public sealed class DaaDamagedImageException : Exception
{
    public DaaDamagedImageException(string detail)
        : base($"The DAA image is damaged or truncated: {detail}")
    {
    }
}
