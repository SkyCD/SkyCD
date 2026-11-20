using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace SkyCD.Plugin.AudioCd;

/// <summary>
/// Reads an audio CD table of contents on Linux through the cdrom driver's ioctl interface, which is available
/// on every current kernel without additional packages. The drive is located from its device node or, for a
/// disc with a mounted filesystem, from the system mount table.
/// </summary>
/// <remarks>
/// The reader follows the kernel's <c>cdrom_tocentry</c> layout: track and MSF request fields, then the start
/// address in minute-second-frame form. Data tracks and unreadable drives are reported as unidentified, so a
/// scan is never interrupted by a drive that is not an audio CD.
/// </remarks>
public sealed class LinuxDiscTocReader : IDiscTocReader
{
    private const string MountTablePath = "/proc/self/mounts";
    private const int OpenReadOnly = 0;
    private const int OpenNonBlocking = 0x800;
    private const nuint ReadTocHeaderRequest = 0x5305; // CDROMREADTOCHDR
    private const nuint ReadTocEntryRequest = 0x5306; // CDROMREADTOCENTRY
    private const byte MsfFormat = 2; // CDROM_MSF
    private const byte LeadOutTrack = 0xAA;
    private const byte ControlDataTrack = 0x04;
    private const int MinTrack = 1;
    private const int MaxTrack = 99;

    public DiscToc? Read(string rootPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

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

        var device = ResolveDevice(rootPath.Trim(), mountTable);
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
            return ReadToc(descriptor, cancellationToken);
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

    /// <summary>Maps a device node or mount point to the device node of the drive that holds it.</summary>
    internal static string? ResolveDevice(string rootPath, string? mountTable)
    {
        if (rootPath.StartsWith("/dev/", StringComparison.Ordinal))
        {
            return File.Exists(rootPath) ? rootPath : null;
        }

        return mountTable is null ? null : FindMountedDevice(mountTable, rootPath);
    }

    /// <summary>Finds the longest mount point containing <paramref name="path"/> and returns its device.</summary>
    internal static string? FindMountedDevice(string mountTable, string path)
    {
        var target = Path.TrimEndingDirectorySeparator(path);
        string? bestMount = null;
        string? bestDevice = null;

        foreach (var line in mountTable.Split('\n'))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
            {
                continue;
            }

            var device = Unescape(fields[0]);
            if (!device.StartsWith("/dev/", StringComparison.Ordinal))
            {
                continue;
            }

            var mountPoint = Path.TrimEndingDirectorySeparator(Unescape(fields[1]));
            if (!IsUnder(target, mountPoint))
            {
                continue;
            }

            if (bestMount is null || mountPoint.Length > bestMount.Length)
            {
                bestMount = mountPoint;
                bestDevice = device;
            }
        }

        return bestDevice;
    }

    /// <summary>Builds the TOC from the per-track start addresses and the lead-out entry.</summary>
    internal static DiscToc? AssembleToc(int firstTrack, IReadOnlyList<DiscTocEntry> tracks, DiscTocEntry leadOut)
    {
        if (tracks.Count is 0 or > MaxTrack || leadOut.IsData)
        {
            return null;
        }

        var offsets = new List<int>(tracks.Count);
        foreach (var track in tracks)
        {
            if (track.IsData)
            {
                // A mixed-mode or CD-Extra layout is not an audio CD; both disc-id schemes would be wrong.
                return null;
            }

            offsets.Add(track.StartFrames);
        }

        var toc = new DiscToc(firstTrack, offsets, leadOut.StartFrames);
        return toc.IsValid() ? toc : null;
    }

    internal static int MsfToFrames(byte minute, byte second, byte frame) =>
        ((minute * 60) + second) * 75 + frame;

    private static DiscToc? ReadToc(int descriptor, CancellationToken cancellationToken)
    {
        var header = new byte[2];
        if (ioctl(descriptor, ReadTocHeaderRequest, header) != 0)
        {
            return null;
        }

        var first = header[0];
        var last = header[1];
        if (first is < MinTrack or > MaxTrack || last < first || last > MaxTrack)
        {
            return null;
        }

        var tracks = new List<DiscTocEntry>(last - first + 1);
        for (var track = first; track <= last; track++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadEntry(descriptor, (byte)track) is not DiscTocEntry entry)
            {
                return null;
            }

            tracks.Add(entry);
        }

        return ReadEntry(descriptor, LeadOutTrack) is DiscTocEntry leadOut
            ? AssembleToc(first, tracks, leadOut)
            : null;
    }

    private static DiscTocEntry? ReadEntry(int descriptor, byte track)
    {
        // struct cdrom_tocentry: track(1), adr:4/ctrl:4(1), format(1), pad(1), msf(6), datamode(1), pad(1).
        var entry = new byte[12];
        entry[0] = track;
        entry[2] = MsfFormat;
        if (ioctl(descriptor, ReadTocEntryRequest, entry) != 0)
        {
            return null;
        }

        var control = (byte)(entry[1] >> 4);
        return new DiscTocEntry(MsfToFrames(entry[4], entry[5], entry[6]), (control & ControlDataTrack) != 0);
    }

    private static bool IsUnder(string path, string root) =>
        string.Equals(path, root, StringComparison.Ordinal) ||
        (path.Length > root.Length && path.StartsWith(root, StringComparison.Ordinal) &&
         path[root.Length] == '/');

    /// <summary>Reverses the octal escapes the mount table uses for spaces and control characters.</summary>
    private static string Unescape(string value) =>
        value.Contains('\\', StringComparison.Ordinal)
            ? value.Replace("\\040", " ", StringComparison.Ordinal)
                .Replace("\\011", "\t", StringComparison.Ordinal)
                .Replace("\\012", "\n", StringComparison.Ordinal)
                .Replace("\\134", "\\", StringComparison.Ordinal)
            : value;

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static extern int ioctl(int descriptor, nuint request, [In, Out] byte[] argument);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int close(int descriptor);
}

/// <summary>A TOC entry's absolute start frame and whether it belongs to a data track.</summary>
internal readonly record struct DiscTocEntry(int StartFrames, bool IsData);
