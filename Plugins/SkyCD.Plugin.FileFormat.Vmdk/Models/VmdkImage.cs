using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DiscUtils;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiscUtils.Ntfs;
using DiscUtils.Setup;
using DiscUtils.Streams;
using DiscUtils.Vmdk;
using SkyCD.Plugin.FileFormat.Vmdk.Exceptions;

namespace SkyCD.Plugin.FileFormat.Vmdk.Models;

/// <summary>
/// One file or folder reported by a file system inside a VMDK image.
/// </summary>
internal sealed record VmdkFileEntry(
    DiscFileSystem FileSystem,
    string RawPath,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc);

/// <summary>
/// An opened VMDK image with the supported file systems found on its partitions. The image resolves
/// companion extents relative to its own path, and every file system stays open until the image is
/// disposed so contained files can be read one at a time.
/// </summary>
internal sealed class VmdkImage : IDisposable
{
    private readonly VirtualDisk disk;
    private readonly IReadOnlyList<VmdkVolume> volumes;
    private readonly string? temporarySourcePath;
    private bool disposed;

    static VmdkImage()
    {
        SetupHelper.RegisterAssembly(typeof(FatFileSystem).Assembly);
        SetupHelper.RegisterAssembly(typeof(NtfsFileSystem).Assembly);
        SetupHelper.RegisterAssembly(typeof(ExtFileSystem).Assembly);
    }

    private VmdkImage(VirtualDisk disk, IReadOnlyList<VmdkVolume> volumes, string? temporarySourcePath)
    {
        this.disk = disk;
        this.volumes = volumes;
        this.temporarySourcePath = temporarySourcePath;
    }

    /// <summary>Gets whether at least one supported file system was found in the image.</summary>
    public bool HasFileSystems => volumes.Count > 0;

    /// <summary>Opens a local image so descriptors can resolve companion extent files.</summary>
    public static VmdkImage OpenFromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EnsureSafeExtents(path);

        var disk = new Disk(path, FileAccess.Read);
        try
        {
            return Create(disk, temporarySourcePath: null);
        }
        catch
        {
            disk.Dispose();
            throw;
        }
    }

    /// <summary>Opens a monolithic sparse image from a seekable stream.</summary>
    public static VmdkImage OpenFromStream(Stream stream, bool ownsStream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var disk = new Disk(stream, ownsStream ? Ownership.Dispose : Ownership.None);
        try
        {
            return Create(disk, temporarySourcePath: null);
        }
        catch
        {
            disk.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens an image from its path when the host supplied one, otherwise from the source stream. A
    /// non-seekable stream is copied to a temporary file that is removed when this image is disposed.
    /// </summary>
    public static VmdkImage Open(Stream source, string? sourcePath)
    {
        if (IsVmdkPath(sourcePath) && File.Exists(sourcePath))
        {
            return OpenFromPath(sourcePath!);
        }

        ArgumentNullException.ThrowIfNull(source);
        if (source.CanSeek)
        {
            return OpenFromStream(source, ownsStream: false);
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"skycd-vmdk-source-{Guid.NewGuid():N}.vmdk");
        try
        {
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(file);
            }

            var disk = new Disk(temporaryPath, FileAccess.Read);
            try
            {
                return Create(disk, temporaryPath);
            }
            catch
            {
                disk.Dispose();
                throw;
            }
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    /// <summary>Opens a contained file for reading directly from the image.</summary>
    public Stream OpenEntry(VmdkFileEntry entry)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return entry.FileSystem.OpenFile(entry.RawPath, FileMode.Open, FileAccess.Read);
    }

    /// <summary>
    /// Enumerates the files and folders of every readable volume. Unsafe, damaged, reparse-point and
    /// oversized-depth entries are skipped, and the configured entry limit bounds the result.
    /// </summary>
    public IEnumerable<VmdkFileEntry> EnumerateEntries(VmdkIndexLimits limits, CancellationToken cancellationToken)
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

                    var entry = TryReadEntry(volume, child, depth);
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
            disk.Dispose();
        }
        catch (Exception)
        {
            // Best-effort release of a damaged disk.
        }

        if (temporarySourcePath is not null)
        {
            TryDeleteFile(temporarySourcePath);
        }
    }

    private static VmdkImage Create(VirtualDisk disk, string? temporarySourcePath)
    {
        var fileSystems = new List<DiscFileSystem>();
        try
        {
            foreach (var volume in VolumeManager.GetPhysicalVolumes(disk))
            {
                var fileSystem = TryOpenFileSystem(volume);
                if (fileSystem is not null)
                {
                    fileSystems.Add(fileSystem);
                }
            }
        }
        catch
        {
            DisposeFileSystems(fileSystems);
            throw;
        }

        return new VmdkImage(disk, BuildVolumes(fileSystems), temporarySourcePath);
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

    private static IReadOnlyList<VmdkVolume> BuildVolumes(IReadOnlyList<DiscFileSystem> fileSystems)
    {
        var volumes = new List<VmdkVolume>(fileSystems.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var useNames = fileSystems.Count > 1;

        for (var index = 0; index < fileSystems.Count; index++)
        {
            var fileSystem = fileSystems[index];
            var name = string.Empty;
            if (useNames)
            {
                var label = TryGetVolumeLabel(fileSystem);
                name = MakeUniqueName(SanitizeName(label), index, names);
            }

            volumes.Add(new VmdkVolume(fileSystem, name));
        }

        return volumes;
    }

    private static VmdkFileEntry? TryReadEntry(VmdkVolume volume, string rawPath, int depth)
    {
        if (!VmdkEntryPath.TryNormalize(rawPath, out var normalized))
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
        return new VmdkFileEntry(volume.FileSystem, rawPath, fullPath, isDirectory, size,
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

    private static bool IsVmdkPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetExtension(path), ".vmdk", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rejects descriptors that point at extents or parent disks outside the image's own directory.
    /// </summary>
    private static void EnsureSafeExtents(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new VmdkUnsafeExtentPathException();

        using var layer = new DiskImageFile(fullPath, FileAccess.Read);

        foreach (var extent in layer.ExtentPaths)
        {
            if (!IsContainedFile(directory, extent))
            {
                throw new VmdkUnsafeExtentPathException();
            }
        }

        if (!layer.NeedsParent)
        {
            return;
        }

        foreach (var parent in layer.GetParentLocations())
        {
            if (!string.IsNullOrWhiteSpace(parent) && !IsContainedFile(directory, parent))
            {
                throw new VmdkUnsafeExtentPathException();
            }
        }
    }

    private static bool IsContainedFile(string directory, string candidate)
    {
        try
        {
            var resolved = Path.GetFullPath(candidate, directory);
            var prefix = directory.EndsWith(Path.DirectorySeparatorChar)
                ? directory
                : directory + Path.DirectorySeparatorChar;
            return resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException
                                              or NotSupportedException or System.Security.SecurityException)
        {
            return false;
        }
    }

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

    private sealed record VmdkVolume(DiscFileSystem FileSystem, string Name);
}
