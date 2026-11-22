using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DiscUtils;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiscUtils.Ntfs;
using DiscUtils.Raw;
using DiscUtils.Setup;
using DiscUtils.Streams;
using SkyCD.Plugin.Metadata.Vdi.Exceptions;
using SkyCD.Plugin.Metadata.Vdi.Parsing;

namespace SkyCD.Plugin.Metadata.Vdi.Models;

/// <summary>
/// One file or folder reported by a file system inside a VirtualBox VDI image.
/// </summary>
internal sealed record VdiFileEntry(
    DiscFileSystem FileSystem,
    string RawPath,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc);

/// <summary>
/// An opened VirtualBox VDI image with the supported file systems found on its partitions. Every file
/// system stays open until the image is disposed so contained files can be read one at a time, and no
/// guest content is ever written to disk by this type.
/// </summary>
internal sealed class VdiImage : IDisposable
{
    private readonly Disk? disk;
    private readonly VdiDiskStream? content;
    private readonly IReadOnlyList<VdiVolume> volumes;
    private bool disposed;

    static VdiImage()
    {
        SetupHelper.RegisterAssembly(typeof(FatFileSystem).Assembly);
        SetupHelper.RegisterAssembly(typeof(NtfsFileSystem).Assembly);
        SetupHelper.RegisterAssembly(typeof(ExtFileSystem).Assembly);
    }

    private VdiImage(Disk? disk, VdiDiskStream? content, IReadOnlyList<VdiVolume> volumes)
    {
        this.disk = disk;
        this.content = content;
        this.volumes = volumes;
    }

    /// <summary>Gets whether at least one supported file system was found in the image.</summary>
    public bool HasFileSystems => volumes.Count > 0;

    /// <summary>Gets the VirtualBox header of the opened image.</summary>
    public VdiHeader? Header => content?.Header;

    /// <summary>Opens a local image through its path.</summary>
    public static VdiImage OpenFromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var content = OpenImageFile(Path.GetFullPath(path));
        try
        {
            return Create(content);
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens an image from its path when the host supplied one, otherwise from the source stream. A
    /// non-seekable stream is copied to a temporary file that is removed when this image is disposed.
    /// Snapshot layers cannot resolve their base image without a local path and are reported as missing
    /// one.
    /// </summary>
    public static VdiImage Open(Stream source, string? sourcePath)
    {
        if (IsVdiPath(sourcePath) && File.Exists(sourcePath))
        {
            return OpenFromPath(sourcePath!);
        }

        ArgumentNullException.ThrowIfNull(source);
        if (source.CanSeek)
        {
            return OpenFromStream(source);
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"skycd-vdi-source-{Guid.NewGuid():N}.vdi");
        var copy = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            81_920, FileOptions.DeleteOnClose);
        try
        {
            source.CopyTo(copy);
            copy.Position = 0;
            return OpenFromStream(copy);
        }
        catch
        {
            copy.Dispose();
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    /// <summary>Opens a contained file for reading directly from the image.</summary>
    public Stream OpenEntry(VdiFileEntry entry)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return entry.FileSystem.OpenFile(entry.RawPath, FileMode.Open, FileAccess.Read);
    }

    /// <summary>
    /// Enumerates the files and folders of every readable volume. Unsafe, damaged, reparse-point and
    /// oversized-depth entries are skipped, and the configured entry limit bounds the result.
    /// </summary>
    public IEnumerable<VdiFileEntry> EnumerateEntries(VdiIndexLimits limits, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();

        var count = 0;

        foreach (var volume in volumes)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { string.Empty };
            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((string.Empty, 0));

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (directory, depth) = pending.Pop();

                string[] children;
                try
                {
                    children = volume.FileSystem.GetFileSystemEntries(directory);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var child in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (count >= limits.MaxEntries)
                    {
                        yield break;
                    }

                    var entry = TryReadEntry(volume, child);
                    if (entry is null)
                    {
                        continue;
                    }

                    count++;
                    yield return entry;

                    if (entry.IsDirectory && depth < limits.MaxDepth && visited.Add(child))
                    {
                        pending.Push((child, depth + 1));
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var volume in volumes)
        {
            try
            {
                volume.FileSystem.Dispose();
            }
            catch (Exception)
            {
                // Best-effort release of a damaged file system.
            }
        }

        try
        {
            disk?.Dispose();
        }
        catch (Exception)
        {
            // Best-effort release of a damaged disk.
        }

        try
        {
            content?.Dispose();
        }
        catch (Exception)
        {
            // Best-effort release of a damaged image stream.
        }
    }

    private static VdiImage OpenFromStream(Stream stream)
    {
        var header = VdiHeader.Read(stream);
        stream.Position = 0;
        var content = new VdiDiskStream(stream, ownsSource: false, header);
        try
        {
            return Create(content);
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    private static VdiDiskStream OpenImageFile(string path)
    {
        FileStream file;
        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            throw new VdiDamagedImageException();
        }

        try
        {
            file.Position = 0;
            var header = VdiHeader.Read(file);
            file.Position = 0;
            return new VdiDiskStream(file, ownsSource: true, header);
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    private static VdiImage Create(VdiDiskStream content)
    {
        if (content.Length == 0)
        {
            return new VdiImage(null, content, []);
        }

        Disk rawDisk;
        try
        {
            rawDisk = new Disk(content, Ownership.None);
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            throw new VdiDamagedImageException();
        }

        var fileSystems = new List<DiscFileSystem>();
        try
        {
            foreach (var volume in VolumeManager.GetPhysicalVolumes(rawDisk))
            {
                var fileSystem = TryOpenFileSystem(volume);
                if (fileSystem is not null)
                {
                    fileSystems.Add(fileSystem);
                }
            }

            if (fileSystems.Count == 0)
            {
                // DiscUtils only reports partitioned volumes; some images hold a file system directly.
                var wholeDisk = TryOpenWholeDiskFileSystem(content);
                if (wholeDisk is not null)
                {
                    fileSystems.Add(wholeDisk);
                }
            }
        }
        catch (Exception exception) when (IsReadFailure(exception))
        {
            DisposeFileSystems(fileSystems);
            throw new VdiDamagedImageException();
        }
        catch
        {
            DisposeFileSystems(fileSystems);
            rawDisk.Dispose();
            throw;
        }

        return new VdiImage(rawDisk, content, BuildVolumes(fileSystems));
    }

    private static DiscFileSystem? TryOpenFileSystem(VolumeInfo volume)
    {
        DiscUtils.FileSystemInfo[] detected;
        try
        {
            detected = FileSystemManager.DetectFileSystems(volume);
        }
        catch (Exception)
        {
            return null;
        }

        foreach (var fileSystemInfo in detected)
        {
            try
            {
                return fileSystemInfo.Open(volume);
            }
            catch (Exception)
            {
                // A damaged or unsupported file system is skipped; other volumes may still be readable.
            }
        }

        return null;
    }

    private static DiscFileSystem? TryOpenWholeDiskFileSystem(Stream content)
    {
        Func<DiscFileSystem>[] openers =
        [
            () => new FatFileSystem(content, Ownership.None),
            () => new ExtFileSystem(content, new FileSystemParameters())
        ];

        foreach (var open in openers)
        {
            try
            {
                content.Position = 0;
                return open();
            }
            catch (Exception)
            {
                // Not this file system; the next probe may still match.
            }
        }

        return null;
    }

    private static IReadOnlyList<VdiVolume> BuildVolumes(IReadOnlyList<DiscFileSystem> fileSystems)
    {
        var volumes = new List<VdiVolume>(fileSystems.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var useNames = fileSystems.Count > 1;

        for (var index = 0; index < fileSystems.Count; index++)
        {
            var fileSystem = fileSystems[index];
            var name = string.Empty;
            if (useNames)
            {
                name = MakeUniqueName(SanitizeName(TryGetVolumeLabel(fileSystem)), index, names);
            }

            volumes.Add(new VdiVolume(fileSystem, name));
        }

        return volumes;
    }

    private static VdiFileEntry? TryReadEntry(VdiVolume volume, string rawPath)
    {
        if (!VdiEntryPath.TryNormalize(rawPath, out var normalized))
        {
            return null;
        }

        FileAttributes attributes;
        try
        {
            attributes = volume.FileSystem.GetAttributes(rawPath);
        }
        catch (Exception)
        {
            return null;
        }

        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
        {
            return null;
        }

        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        long size = 0;
        if (!isDirectory)
        {
            try
            {
                size = Math.Max(0, volume.FileSystem.GetFileLength(rawPath));
            }
            catch (Exception)
            {
                return null;
            }
        }

        var fullPath = volume.Name.Length == 0 ? normalized : $"{volume.Name}/{normalized}";
        return new VdiFileEntry(volume.FileSystem, rawPath, fullPath, isDirectory, size,
            TryGetModified(volume.FileSystem, rawPath));
    }

    private static DateTimeOffset? TryGetModified(DiscFileSystem fileSystem, string path)
    {
        try
        {
            var modified = fileSystem.GetLastWriteTimeUtc(path);
            return new DateTimeOffset(DateTime.SpecifyKind(modified, DateTimeKind.Utc));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryGetVolumeLabel(DiscFileSystem fileSystem)
    {
        try
        {
            return fileSystem.VolumeLabel;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string SanitizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = new string(value.Trim()
            .Select(character => character is '/' or '\\' or ':' || char.IsControl(character) ? '_' : character)
            .ToArray());
        return sanitized.Length > 64 ? sanitized[..64] : sanitized;
    }

    private static string MakeUniqueName(string name, int index, HashSet<string> names)
    {
        var candidate = name.Length == 0 ? $"Volume{index + 1}" : name;
        var suffix = 2;
        while (!names.Add(candidate))
        {
            candidate = $"{name} ({suffix++})";
        }

        return candidate;
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is IOException or InvalidDataException or NotSupportedException
            or ArgumentException or InvalidOperationException or FormatException or OverflowException;

    private static bool IsVdiPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetExtension(path), ".vdi", StringComparison.OrdinalIgnoreCase);

    private static void DisposeFileSystems(IEnumerable<DiscFileSystem> fileSystems)
    {
        foreach (var fileSystem in fileSystems)
        {
            try
            {
                fileSystem.Dispose();
            }
            catch (Exception)
            {
                // Best-effort release of a damaged file system.
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }

    private sealed record VdiVolume(DiscFileSystem FileSystem, string Name);
}
