using System.IO;

namespace SkyCD.Presentation.Scanning;

public sealed class MediaDriveInaccessibleException : IOException
{
    public MediaDriveInaccessibleException(string driveName, string reason)
        : base($"The media drive '{driveName}' is inaccessible: {reason}.")
    {
    }
}
