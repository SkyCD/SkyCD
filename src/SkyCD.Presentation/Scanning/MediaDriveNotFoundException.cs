using System.IO;

namespace SkyCD.Presentation.Scanning;

public sealed class MediaDriveNotFoundException : DriveNotFoundException
{
    public MediaDriveNotFoundException(string driveName)
        : base($"The specified media drive was not found: '{driveName}'.")
    {
    }
}
