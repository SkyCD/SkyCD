using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using SkyCD.Plugin.Metadata.AudioCD.Models;

namespace SkyCD.Plugin.Metadata.AudioCD.Parsing;

/// <summary>
/// Reads an audio CD table of contents on macOS through the IOKit optical device interface: the media's BSD
/// device node is located with IOKit, then the drive reports its TOC through the <c>DKIOCCDREADTOC</c> ioctl.
/// No native shim or extra package is required.
/// </summary>
/// <remarks>
/// The TOC arrives in the SCSI layout the block storage driver publishes: a big-endian data length followed by
/// the session numbers and eleven-byte descriptors whose start address is in minute-second-frame form. Raw
/// device access may require the user to be in the operator group; a refused open or ioctl is reported as
/// unidentified rather than as an error.
/// </remarks>
public sealed class MacDiscTocReader : IDiscTocReader
{
    private const int OpenReadOnly = 0;
    private const int OpenNonBlocking = 0x4;
    private const ulong ReadTocRequest = 0xC0186464; // _IOWR('d', 100, dk_cd_read_toc_t) with 24-byte LP64 payload
    private const int RequestLength = 24;
    private const int TocBufferLength = 4096;
    private const int BufferLengthOffset = 14;
    private const int BufferPointerOffset = 16;
    private const byte TocFormat = 0; // CD TOC, descriptors reported as MSF
    private const int DescriptorSize = 11;
    private const byte LeadOutPoint = 0xAA;
    private const byte ControlDataTrack = 0x04;
    private const int MinPoint = 1;
    private const int MaxPoint = 99;
    private const uint Utf8Encoding = 0x08000100;

    public DiscToc? Read(string rootPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS() || string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var device = ResolveDevice(rootPath.Trim());
        if (device is null)
        {
            return null;
        }

        var descriptor = open(device, OpenReadOnly | OpenNonBlocking);
        if (descriptor < 0)
        {
            return null;
        }

        try
        {
            return ReadToc(descriptor);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                           !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            _ = close(descriptor);
        }
    }

    /// <summary>Prefers the raw node of the given device, or the first optical media IOKit reports.</summary>
    internal static string? ResolveDevice(string rootPath)
    {
        if (rootPath.StartsWith("/dev/", StringComparison.Ordinal))
        {
            var raw = ToRawDevice(rootPath);
            return File.Exists(raw) ? raw : null;
        }

        foreach (var bsdName in EnumerateOpticalMedia())
        {
            var raw = "/dev/r" + bsdName;
            if (File.Exists(raw))
            {
                return raw;
            }
        }

        return null;
    }

    internal static string ToRawDevice(string devicePath) =>
        devicePath.StartsWith("/dev/r", StringComparison.Ordinal)
            ? devicePath
            : devicePath.Replace("/dev/", "/dev/r", StringComparison.Ordinal);

    /// <summary>Parses the CDTOC buffer published by the block storage driver.</summary>
    internal static DiscToc? ParseToc(ReadOnlySpan<byte> toc, int length)
    {
        // The length counts the session bytes and descriptors that follow it, not the two length bytes.
        var end = Math.Min(toc.Length, length + 2);
        var offsets = new List<int>();
        var firstTrack = 0;
        int? leadOut = null;

        for (var offset = 4; offset + DescriptorSize <= end; offset += DescriptorSize)
        {
            var control = (byte)(toc[offset + 1] & 0x0F);
            var point = toc[offset + 3];
            var frames = MsfToFrames(toc[offset + 8], toc[offset + 9], toc[offset + 10]);

            if (point == LeadOutPoint)
            {
                leadOut = frames;
                continue;
            }

            if (point is < MinPoint or > MaxPoint)
            {
                continue;
            }

            if ((control & ControlDataTrack) != 0)
            {
                return null;
            }

            if (offsets.Count == 0)
            {
                firstTrack = point;
            }

            offsets.Add(frames);
        }

        if (leadOut is not int leadOutFrames)
        {
            return null;
        }

        var result = new DiscToc(firstTrack, offsets, leadOutFrames);
        return result.IsValid() ? result : null;
    }

    internal static int MsfToFrames(byte minute, byte second, byte frame) =>
        ((minute * 60) + second) * 75 + frame;

    private static DiscToc? ReadToc(int descriptor)
    {
        var request = Marshal.AllocHGlobal(RequestLength);
        var data = Marshal.AllocHGlobal(TocBufferLength);
        try
        {
            for (var index = 0; index < RequestLength; index++)
            {
                Marshal.WriteByte(request, index, 0);
            }

            Marshal.WriteByte(request, 0, TocFormat);
            Marshal.WriteByte(request, 1, 1); // formatAsTime
            Marshal.WriteInt16(request, BufferLengthOffset, unchecked((short)TocBufferLength));
            Marshal.WriteIntPtr(request, BufferPointerOffset, data);

            if (ioctl(descriptor, ReadTocRequest, request) != 0)
            {
                return null;
            }

            var length = (Marshal.ReadByte(data, 0) << 8) | Marshal.ReadByte(data, 1);
            if (length < 2 || length > TocBufferLength - 2)
            {
                return null;
            }

            var buffer = new byte[length + 2];
            Marshal.Copy(data, buffer, 0, buffer.Length);
            return ParseToc(buffer, length);
        }
        finally
        {
            Marshal.FreeHGlobal(request);
            Marshal.FreeHGlobal(data);
        }
    }

    private static List<string> EnumerateOpticalMedia()
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

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "open", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl(int descriptor, ulong request, IntPtr argument);

    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "close")]
    private static extern int close(int descriptor);

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
