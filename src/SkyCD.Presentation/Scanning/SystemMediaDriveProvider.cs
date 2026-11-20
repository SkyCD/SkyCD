using System;
using System.Collections.Generic;
using System.IO;

namespace SkyCD.Presentation.Scanning;

public sealed class SystemMediaDriveProvider : IMediaDriveProvider
{
    public event EventHandler? DrivesChanged
    {
        add { }
        remove { }
    }

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

        try
        {
            if (OperatingSystem.IsLinux())
            {
                result.AddRange(LinuxOpticalDriveProbe.GetDrives());
            }
            else if (OperatingSystem.IsMacOS())
            {
                result.AddRange(MacOpticalDriveProbe.GetDrives());
            }
        }
        catch
        {
            // Optical probe errors must never hide the mounted drives above.
        }

        return result;
    }

    public MediaDriveInfo? GetDrive(string drivePathOrName)
    {
        if (string.IsNullOrWhiteSpace(drivePathOrName))
        {
            return null;
        }

        // An unmounted optical disc is addressed by its device node, which DriveInfo cannot describe.
        if (drivePathOrName.StartsWith("/dev/", StringComparison.Ordinal))
        {
            return OperatingSystem.IsLinux() ? LinuxOpticalDriveProbe.GetDrive(drivePathOrName)
                : OperatingSystem.IsMacOS() ? MacOpticalDriveProbe.GetDrive(drivePathOrName)
                : null;
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
