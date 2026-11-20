using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Lists optical drives on macOS through IOKit. An audio CD has no filesystem volume, so it never appears in
/// <see cref="DriveInfo.GetDrives"/>; IOKit publishes the media's BSD device node as soon as a disc is present,
/// which is what the scanner and the audio CD reader address.
/// </summary>
internal static class MacOpticalDriveProbe
{
    private const uint Utf8Encoding = 0x08000100;

    public static IReadOnlyList<MediaDriveInfo> GetDrives()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return [];
        }

        var drives = new List<MediaDriveInfo>();
        try
        {
            foreach (var bsdName in EnumerateMediaNames())
            {
                var device = "/dev/r" + bsdName;
                if (File.Exists(device))
                {
                    drives.Add(new MediaDriveInfo(device, null, device, DriveType.CDRom, true, 0, 0, 0));
                }
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // IOKit is always present on macOS; a failed probe simply lists no extra drives.
        }

        return drives;
    }

    public static MediaDriveInfo? GetDrive(string devicePath)
    {
        if (!OperatingSystem.IsMacOS() || !devicePath.StartsWith("/dev/", StringComparison.Ordinal) ||
            !File.Exists(devicePath))
        {
            return null;
        }

        return new MediaDriveInfo(devicePath, null, devicePath, DriveType.CDRom, true, 0, 0, 0);
    }

    private static List<string> EnumerateMediaNames()
    {
        var names = new List<string>();
        if (IOMasterPort(0, out var mainPort) != 0)
        {
            return names;
        }

        var matching = IOServiceMatching("IOCDMedia");
        if (matching == IntPtr.Zero)
        {
            return names;
        }

        // IOServiceGetMatchingServices consumes the matching dictionary.
        if (IOServiceGetMatchingServices(mainPort, matching, out var iterator) != 0 || iterator == 0)
        {
            return names;
        }

        try
        {
            var key = CFStringCreateWithCString(IntPtr.Zero, "BSD Name", Utf8Encoding);
            if (key == IntPtr.Zero)
            {
                return names;
            }

            try
            {
                uint service;
                while ((service = IOIteratorNext(iterator)) != 0)
                {
                    try
                    {
                        var name = ReadBsdName(service, key);
                        if (!string.IsNullOrEmpty(name))
                        {
                            names.Add(name);
                        }
                    }
                    finally
                    {
                        _ = IOObjectRelease(service);
                    }
                }
            }
            finally
            {
                CFRelease(key);
            }
        }
        finally
        {
            _ = IOObjectRelease(iterator);
        }

        return names;
    }

    private static string? ReadBsdName(uint service, IntPtr key)
    {
        var property = IORegistryEntryCreateCFProperty(service, key, IntPtr.Zero, 0);
        if (property == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new byte[256];
            if (!CFStringGetCString(property, buffer, buffer.Length, Utf8Encoding))
            {
                return null;
            }

            var terminator = Array.IndexOf(buffer, (byte)0);
            return Encoding.UTF8.GetString(buffer, 0, terminator < 0 ? buffer.Length : terminator);
        }
        finally
        {
            CFRelease(property);
        }
    }

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", EntryPoint = "IOMasterPort")]
    private static extern int IOMasterPort(uint bootstrapPort, out uint mainPort);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", EntryPoint = "IOServiceMatching")]
    private static extern IntPtr IOServiceMatching([MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", EntryPoint = "IOServiceGetMatchingServices")]
    private static extern int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", EntryPoint = "IOIteratorNext")]
    private static extern uint IOIteratorNext(uint iterator);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", EntryPoint = "IOObjectRelease")]
    private static extern int IOObjectRelease(uint @object);

    [DllImport("/System/Library/Frameworks/IOKit.framework/IOKit", EntryPoint = "IORegistryEntryCreateCFProperty")]
    private static extern IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation",
        EntryPoint = "CFStringCreateWithCString")]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", EntryPoint = "CFStringGetCString")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, long bufferSize, uint encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", EntryPoint = "CFRelease")]
    private static extern void CFRelease(IntPtr value);
}
