using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Lists optical drives on Linux through sysfs. An audio CD has no filesystem mount, so it never appears in
/// <see cref="DriveInfo.GetDrives"/>; the device node and the drive's media status are the only way to offer it
/// for scanning. Drives without a readable disc and drives already represented by a mount are skipped.
/// </summary>
internal static class LinuxOpticalDriveProbe
{
    private const string DefaultSysfsRoot = "/sys/class/block";
    private const string DefaultDeviceRoot = "/dev";
    private const string MountTablePath = "/proc/self/mounts";
    private const int OpenReadOnly = 0;
    private const int OpenNonBlocking = 0x800;
    private const nuint DiscStatusRequest = 0x5327; // CDROM_DISC_STATUS
    private const int DiscInfoUnavailable = 0;
    private const int NoDisc = 1;
    private const int TrayOpen = 2;
    private const int DriveNotReady = 3;

    public static IReadOnlyList<MediaDriveInfo> GetDrives()
    {
        if (!OperatingSystem.IsLinux())
        {
            return [];
        }

        string? mountTable = null;
        try
        {
            if (File.Exists(MountTablePath))
            {
                mountTable = File.ReadAllText(MountTablePath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            mountTable = null;
        }

        return GetDrives(DefaultSysfsRoot, DefaultDeviceRoot, mountTable, HasMedia);
    }

    /// <summary>Builds the drive entries from a sysfs tree; <paramref name="hasMedia"/> isolates the ioctl.</summary>
    internal static IReadOnlyList<MediaDriveInfo> GetDrives(string sysfsRoot, string deviceRoot,
        string? mountTable, Func<string, bool> hasMedia)
    {
        var drives = new List<MediaDriveInfo>();
        if (!Directory.Exists(sysfsRoot))
        {
            return drives;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.GetDirectories(sysfsRoot, "sr*");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return drives;
        }

        foreach (var entry in entries)
        {
            var name = Path.GetFileName(entry);
            if (name.Length <= 2 || !int.TryParse(name.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture,
                    out _))
            {
                continue;
            }

            var device = Path.Combine(deviceRoot, name);
            if (!File.Exists(device) || IsMounted(device, mountTable) || !hasMedia(device))
            {
                continue;
            }

            drives.Add(new MediaDriveInfo(device, null, device, DriveType.CDRom, true, 0, 0, 0));
        }

        return drives;
    }

    /// <summary>Returns a ready drive entry for a device node the user selected directly.</summary>
    public static MediaDriveInfo? GetDrive(string devicePath)
    {
        if (!OperatingSystem.IsLinux() || !devicePath.StartsWith("/dev/", StringComparison.Ordinal) ||
            !File.Exists(devicePath) || !HasMedia(devicePath))
        {
            return null;
        }

        return new MediaDriveInfo(devicePath, null, devicePath, DriveType.CDRom, true, 0, 0, 0);
    }

    internal static bool IsMounted(string device, string? mountTable)
    {
        if (mountTable is null)
        {
            return false;
        }

        foreach (var line in mountTable.Split('\n'))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length >= 2 && string.Equals(Unescape(fields[0]), device, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMedia(string device)
    {
        var descriptor = open(device, OpenReadOnly | OpenNonBlocking);
        if (descriptor < 0)
        {
            return false;
        }

        try
        {
            var status = -1;
            if (ioctl(descriptor, DiscStatusRequest, ref status) != 0)
            {
                return false;
            }

            return status is not (DiscInfoUnavailable or NoDisc or TrayOpen or DriveNotReady);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            _ = close(descriptor);
        }
    }

    private static string Unescape(string value) =>
        value.Contains('\\', StringComparison.Ordinal)
            ? value.Replace("\\040", " ", StringComparison.Ordinal).Replace("\\134", "\\", StringComparison.Ordinal)
            : value;

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl(int descriptor, nuint request, ref int argument);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int close(int descriptor);
}
