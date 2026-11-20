using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace SkyCD.App;

/// <summary>
/// Ejects an optical device on Linux (<c>CDROMEJECT</c>) and on macOS (<c>DKIOCEJECT</c>). The device is opened
/// read-only and non-blocking so an idle or missing drive cannot hang the finished scan.
/// </summary>
internal static class UnixOpticalEjector
{
#if WINDOWS
    private const string LibcName = "msvcrt";
#else
    private const string LibcName = "libc";
#endif

    private const int OpenReadOnlyLinux = 0x0000;
    private const int OpenNonBlockingLinux = 0x0800;
    private const int OpenReadOnlyMacOs = 0x0000;
    private const int OpenNonBlockingMacOs = 0x0004;
    private const int Ejected = 0;
    private const int TrayAlreadyOpen = 1;
    private const int NotEjected = -1;
    private const nuint IoctlEjectLinux = 0x5309; // CDROMEJECT
    private const nuint IoctlEjectMacOs = 0x20006415; // DKIOCEJECT

    internal static bool TryEject(string devicePath)
    {
#if WINDOWS
        // Windows uses IOCTL_STORAGE_EJECT_MEDIA through WindowsOpticalEjector.
        _ = devicePath;
        return false;
#else
        if (OperatingSystem.IsLinux())
        {
            return EjectLinux(devicePath);
        }

        if (OperatingSystem.IsMacOS())
        {
            return EjectMacOs(devicePath);
        }

        return false;
#endif
    }

#if !WINDOWS
    private static bool EjectLinux(string devicePath)
    {
        var descriptor = open(devicePath, OpenReadOnlyLinux | OpenNonBlockingLinux);
        if (descriptor < 0)
        {
            return false;
        }

        try
        {
            return ioctl(descriptor, IoctlEjectLinux, IntPtr.Zero) == Ejected;
        }
        finally
        {
            _ = close(descriptor);
        }
    }

    private static bool EjectMacOs(string devicePath)
    {
        var descriptor = open(devicePath, OpenReadOnlyMacOs | OpenNonBlockingMacOs);
        if (descriptor < 0)
        {
            return false;
        }

        try
        {
            return ioctl(descriptor, IoctlEjectMacOs, IntPtr.Zero) == Ejected;
        }
        finally
        {
            _ = close(descriptor);
        }
    }

    [DllImport(LibcName, EntryPoint = "open", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport(LibcName, EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl(int descriptor, nuint request, IntPtr argument);

    [DllImport(LibcName, EntryPoint = "close")]
    private static extern int close(int descriptor);
#endif
}
