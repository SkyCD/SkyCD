using System.Collections.Generic;
using System.IO;

namespace SkyCD.Presentation.Scanning;

public sealed class SystemMediaDriveProvider : IMediaDriveProvider
{
    public IReadOnlyList<MediaDriveInfo> GetSupportedDrives()
    {
        var result = new List<MediaDriveInfo>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (IsSupported(drive))
                {
                    result.Add(CreateMediaDriveInfo(drive));
                }
            }
        }
        catch
        {
            // Ignore system enumeration errors
        }

        return result;
    }

    public MediaDriveInfo? GetDrive(string drivePathOrName)
    {
        if (string.IsNullOrWhiteSpace(drivePathOrName))
        {
            return null;
        }

        try
        {
            var drive = new DriveInfo(drivePathOrName);
            return CreateMediaDriveInfo(drive);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSupported(DriveInfo drive)
    {
        try
        {
            return drive.DriveType is DriveType.CDRom or DriveType.Removable or DriveType.Unknown;
        }
        catch
        {
            return false;
        }
    }

    private static MediaDriveInfo CreateMediaDriveInfo(DriveInfo drive)
    {
        var isReady = false;
        string? volumeLabel = null;
        long availableFreeSpace = 0;
        long totalFreeSpace = 0;
        long totalSize = 0;
        string? driveFormat = null;

        try
        {
            isReady = drive.IsReady;
            if (isReady)
            {
                volumeLabel = drive.VolumeLabel;
                availableFreeSpace = drive.AvailableFreeSpace;
                totalFreeSpace = drive.TotalFreeSpace;
                totalSize = drive.TotalSize;
                driveFormat = drive.DriveFormat;
            }
        }
        catch
        {
            // Transient drive access issues
        }

        return new MediaDriveInfo(
            drive.Name,
            volumeLabel,
            drive.RootDirectory.FullName,
            drive.DriveType,
            isReady,
            availableFreeSpace,
            totalFreeSpace,
            totalSize,
            driveFormat);
    }
}
