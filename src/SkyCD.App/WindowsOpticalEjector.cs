using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace SkyCD.App;

/// <summary>
/// Ejects a disc tray on Windows with <c>IOCTL_STORAGE_EJECT_MEDIA</c>. The volume is first checked for open
/// handles (<c>IOCTL_STORAGE_CHECK_VERIFY</c>); a volume still in use is left mounted instead of forcing an eject.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsOpticalEjector
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlStorageEjectMedia = 0x2D4808;
    private const uint IoctlStorageCheckVerify = 0x2D1400;

    public static bool TryEject(string driveRoot)
    {
        var devicePath = @"\\.\" + driveRoot.TrimEnd('\\', '/');
        using var handle = CreateFile(devicePath, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
            IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return false;
        }

        if (DeviceIoControl(handle, IoctlStorageCheckVerify, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
        {
            return false;
        }

        return DeviceIoControl(handle, IoctlStorageEjectMedia, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        uint inBufferSize,
        IntPtr outBuffer,
        uint outBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);
}
