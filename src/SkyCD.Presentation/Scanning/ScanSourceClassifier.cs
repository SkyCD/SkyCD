using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Classifies a scan root into an I/O profile. Drive type alone cannot separate a floppy from a fast
/// USB stick or a spinning disk from an SSD, so format, capacity and (on Windows) the seek-penalty
/// property refine the guess; the adaptive controller corrects the rest at runtime.
/// </summary>
public static class ScanSourceClassifier
{
    private const long LegacyRemovableFormatLimitBytes = 512L * 1024 * 1024;
    private const long SlowRemovableCapacityLimitBytes = 64L * 1024 * 1024;

    public static ScanSourceProfile FromDrive(MediaDriveInfo? drive)
    {
        if (drive is null)
        {
            return ScanSourceProfile.Unknown;
        }

        return FromDriveType(
            drive.DriveType,
            drive.DriveFormat,
            drive.TotalSize,
            drive.RootDirectory);
    }

    public static ScanSourceProfile FromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ScanSourceProfile.Unknown;
        }

        var trimmed = path.Trim();
        if (IsRemotePath(trimmed))
        {
            return ScanSourceProfile.Network;
        }

        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(trimmed));
            if (string.IsNullOrWhiteSpace(root) || IsRemotePath(root) || !DriveInfo.GetDrives()
                    .Any(drive => string.Equals(drive.Name.TrimEnd('\\', '/'), root.TrimEnd('\\', '/'),
                        StringComparison.OrdinalIgnoreCase)))
            {
                return ScanSourceProfile.Unknown;
            }

            var drive = new DriveInfo(root);
            return FromDriveType(drive.DriveType, SafeFormat(drive), SafeTotalSize(drive), root);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException
                                              or UnauthorizedAccessException or NotSupportedException
                                              or System.Security.SecurityException)
        {
            return ScanSourceProfile.Unknown;
        }
    }

    public static ScanSourceProfile FromDriveType(
        DriveType driveType,
        string? format,
        long totalSize,
        string? rootPath = null)
    {
        var kind = Classify(driveType, format, totalSize, rootPath is null ? null : WindowsSeekPenaltyProbe.TryRead(rootPath));
        return new ScanSourceProfile(kind, totalSize > 0 ? totalSize : null, format);
    }

    public static ScanSourceKind Classify(
        DriveType driveType,
        string? format,
        long totalSize,
        bool? incursSeekPenalty)
    {
        return driveType switch
        {
            DriveType.CDRom => ScanSourceKind.Optical,
            DriveType.Network => ScanSourceKind.Network,
            DriveType.Removable => ClassifyRemovable(format, totalSize),
            DriveType.Fixed => incursSeekPenalty == false ? ScanSourceKind.SolidState : ScanSourceKind.HardDisk,
            _ => ScanSourceKind.Unknown
        };
    }

    private static ScanSourceKind ClassifyRemovable(string? format, long totalSize)
    {
        if (string.Equals(format, "FAT12", StringComparison.OrdinalIgnoreCase))
        {
            return ScanSourceKind.SlowRemovable;
        }

        if (totalSize <= 0)
        {
            return ScanSourceKind.Unknown;
        }

        if (totalSize <= SlowRemovableCapacityLimitBytes)
        {
            return ScanSourceKind.SlowRemovable;
        }

        if (!string.IsNullOrWhiteSpace(format) &&
            format.StartsWith("FAT", StringComparison.OrdinalIgnoreCase) &&
            totalSize <= LegacyRemovableFormatLimitBytes)
        {
            return ScanSourceKind.SlowRemovable;
        }

        // Fast USB sticks and flash card readers behave like local solid-state storage.
        return ScanSourceKind.SolidState;
    }

    private static bool IsRemotePath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith("//", StringComparison.Ordinal) ||
        path.StartsWith("smb://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("webdav://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("webdavs://", StringComparison.OrdinalIgnoreCase);

    private static string? SafeFormat(DriveInfo drive)
    {
        try
        {
            return drive.IsReady ? drive.DriveFormat : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long SafeTotalSize(DriveInfo drive)
    {
        try
        {
            return drive.IsReady ? drive.TotalSize : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Reads the storage seek-penalty property on Windows so fixed volumes can be told apart from
    /// spinning disks. Other platforms report <c>null</c> and stay conservative.
    /// </summary>
    private static class WindowsSeekPenaltyProbe
    {
        private const uint IoctlStorageQueryProperty = 0x002D1400;
        private const int StorageDeviceSeekPenaltyProperty = 7;
        private const int PropertyStandardQuery = 0;
        private const uint FileShareRead = 0x1;
        private const uint FileShareWrite = 0x2;
        private const uint OpenExisting = 3;

        public static bool? TryRead(string rootPath)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            try
            {
                var device = ToDevicePath(rootPath);
                if (device is null)
                {
                    return null;
                }

                using var handle = CreateFile(device, 0, FileShareRead | FileShareWrite, IntPtr.Zero,
                    OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    return null;
                }

                var query = new StoragePropertyQuery
                {
                    PropertyId = StorageDeviceSeekPenaltyProperty,
                    QueryType = PropertyStandardQuery
                };
                var buffer = new byte[512];
                if (!DeviceIoControl(handle, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(),
                        buffer, buffer.Length, out var returned, IntPtr.Zero) || returned < Marshal.SizeOf<DeviceSeekPenaltyDescriptor>())
                {
                    return null;
                }

                var descriptor = MemoryMarshal.Read<DeviceSeekPenaltyDescriptor>(buffer);
                return descriptor.IncursSeekPenalty != 0;
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException
                                                  or ArgumentException or IOException)
            {
                return null;
            }
        }

        private static string? ToDevicePath(string rootPath)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(rootPath));
            if (string.IsNullOrWhiteSpace(root) || root.Length < 2 || !char.IsLetter(root[0]) || root[1] != ':')
            {
                return null;
            }

            return @"\\.\" + char.ToUpperInvariant(root[0]) + ":";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StoragePropertyQuery
        {
            public int PropertyId;
            public int QueryType;
            public byte AdditionalParameters;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceSeekPenaltyDescriptor
        {
            public uint Version;
            public uint Size;
            public byte IncursSeekPenalty;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            Microsoft.Win32.SafeHandles.SafeFileHandle device,
            uint controlCode,
            ref StoragePropertyQuery input,
            int inputSize,
            byte[] output,
            int outputSize,
            out int bytesReturned,
            IntPtr overlapped);
    }
}
