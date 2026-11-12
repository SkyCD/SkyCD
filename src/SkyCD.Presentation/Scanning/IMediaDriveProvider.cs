using System.Collections.Generic;

namespace SkyCD.Presentation.Scanning;

public interface IMediaDriveProvider
{
    IReadOnlyList<MediaDriveInfo> GetSupportedDrives();

    MediaDriveInfo? GetDrive(string drivePathOrName);
}
