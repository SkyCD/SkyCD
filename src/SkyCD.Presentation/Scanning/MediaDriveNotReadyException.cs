using System.IO;

namespace SkyCD.Presentation.Scanning;

public sealed class MediaDriveNotReadyException : IOException
{
    public MediaDriveNotReadyException(string driveName)
        : base($"The media drive '{driveName}' is not ready or has been ejected.")
    {
    }
}
