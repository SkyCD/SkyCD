using System.IO;

namespace SkyCD.Presentation.Scanning;

public sealed record MediaDriveInfo(
    string Name,
    string? VolumeLabel,
    string RootDirectory,
    DriveType DriveType,
    bool IsReady,
    long AvailableFreeSpace,
    long TotalFreeSpace,
    long TotalSize,
    string? DriveFormat = null)
{
    public string DisplayName
    {
        get
        {
            var cleanName = Name.TrimEnd('\\');
            return !string.IsNullOrWhiteSpace(VolumeLabel)
                ? $"{cleanName} [{VolumeLabel}]"
                : cleanName;
        }
    }
}
