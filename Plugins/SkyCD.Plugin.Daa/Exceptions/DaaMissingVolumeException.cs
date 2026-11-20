using System;

namespace SkyCD.Plugin.Daa.Exceptions;

public sealed class DaaMissingVolumeException : Exception
{
    public DaaMissingVolumeException(string volumeName)
        : base($"The companion DAA volume '{volumeName}' is missing.")
    {
    }
}
