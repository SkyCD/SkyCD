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
using SkyCD.Plugin.Metadata.DiskImages.Models;
using SkyCD.Plugin.Metadata.DiskImages.Vhd.Exceptions;
using VhdDisk = DiscUtils.Vhd.Disk;
using VhdDiskImageFile = DiscUtils.Vhd.DiskImageFile;
using VhdxDisk = DiscUtils.Vhdx.Disk;
using VhdxDiskImageFile = DiscUtils.Vhdx.DiskImageFile;

namespace SkyCD.Plugin.Metadata.DiskImages.Vhd.Models;

/// <summary>
/// One file or folder reported by a file system inside a VHD or VHDX image.
/// </summary>
internal sealed record VhdFileEntry(
    DiscFileSystem FileSystem,
    string RawPath,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc);

/// <summary>
/// An opened VHD or VHDX image with the supported file systems found on its partitions. Differencing
/// images resolve their parent chain relative to their own directory, and every file system stays open
/// until the image is disposed so contained files can be read one at a time.
/// </summary>
internal sealed class VhdImage : IDisposable
{
    private const int MaxParentDepth = 8;

    private readonly DiscUtils.VirtualDisk disk;
    private readonly Stream? ownedStream;
    private readonly IReadOnlyList<VhdVolume> volumes;
    private bool disposed;

    static VhdImage()
    {
        SetupHelper.RegisterAssembly(typeof(FatFileSystem).Assembly);
        SetupHelper.RegisterAssembly(typeof(NtfsFileSystem).Assembly);
        SetupHelper.RegisterAssembly(typeof(ExtFileSystem).Assembly);
    }

    private VhdImage(DiscUtils.VirtualDisk disk, Stream? ownedStream, IReadOnlyList<VhdVolume> volumes)
    {
        this.disk = disk;
        this.ownedStream = ownedStream;
        this.volumes = volumes;
    }

    /// <summary>Gets whether at least one supported file system was found in the image.</summary>
    public bool HasFileSystems => volumes.Count > 0;

    /// <summary>Opens a local image so its parent chain can be resolved relative to its own directory.</summary>
    public static VhdImage OpenFromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var format = DetectFormat(fullPath);
        DiscUtils.VirtualDisk disk;
        if (format == ImageFormat.Vhdx)
        {
            disk = new VhdxDisk(OpenVhdxChain(fullPath, depth: 0, new HashSet<string>(PathComparer)),
                Ownership.Dispose);
        }
        else
        {
            disk = new VhdDisk(OpenVhdChain(fullPath, depth: 0, new HashSet<string>(PathComparer)),
                Ownership.Dispose);
        }

        try
        {
            return Create(disk, ownedStream: null);
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
    /// Images opened without a path cannot resolve a parent file and are reported as missing one.
    /// </summary>
    public static VhdImage Open(Stream source, string? sourcePath)
    {
        if (IsImagePath(sourcePath) && File.Exists(sourcePath))
        {
            return OpenFromPath(sourcePath!);
        }

        ArgumentNullException.ThrowIfNull(source);
        if (source.CanSeek)
        {
            return OpenFromStream(source, ownsStream: false);
        }

        var extension = string.Equals(Path.GetExtension(sourcePath), ".vhdx", StringComparison.OrdinalIgnoreCase)
            ? ".vhdx"
            : ".vhd";
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"skycd-vhd-source-{Guid.NewGuid():N}{extension}");
        var copy = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            81_920, FileOptions.DeleteOnClose);
        try
        {
            source.CopyTo(copy);
            copy.Position = 0;
            return OpenFromStream(copy, ownsStream: true);
        }
        catch
        {
            copy.Dispose();
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    /// <summary>Opens a contained file for reading directly from the image.</summary>
    public Stream OpenEntry(VhdFileEntry entry)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return entry.FileSystem.OpenFile(entry.RawPath, FileMode.Open, FileAccess.Read);
    }

    /// <summary>
    /// Enumerates the files and folders of every readable volume. Unsafe, damaged, reparse-point and
    /// oversized-depth entries are skipped, and the configured entry limit bounds the result.
    /// </summary>
    public IEnumerable<VhdFileEntry> EnumerateEntries(DiskImageIndexLimits limits, CancellationToken cancellationToken)
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

                    if (entry.IsDirectory && depth < (limits.MaxDepth ?? DiskImageIndexLimits.FileSystemMaxDepth) && visited.Add(child))
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

        try
        {
            ownedStream?.Dispose();
        }
        catch (Exception)
        {
            // Best-effort release of a damaged image stream.
        }
    }

    private static VhdImage OpenFromStream(Stream stream, bool ownsStream)
    {
        var format = DetectFormat(stream);
        stream.Position = 0;
        DiscUtils.VirtualDisk disk;
        if (format == ImageFormat.Vhdx)
        {
            var file = new VhdxDiskImageFile(stream, Ownership.None);
            if (file.NeedsParent)
            {
                file.Dispose();
                throw new VhdMissingParentException();
            }

            disk = new VhdxDisk(new List<VhdxDiskImageFile> { file }, Ownership.Dispose);
        }
        else
        {
            var file = new VhdDiskImageFile(stream, Ownership.None);
            if (file.NeedsParent)
            {
                file.Dispose();
                throw new VhdMissingParentException();
            }

            disk = new VhdDisk(new List<VhdDiskImageFile> { file }, Ownership.Dispose);
        }

        try
        {
            return Create(disk, ownsStream ? stream : null);
        }
        catch
        {
            disk.Dispose();
            if (ownsStream)
            {
                try
                {
                    stream.Dispose();
                }
                catch (Exception)
                {
                    // Best-effort release of a damaged image stream.
                }
            }

            throw;
        }
    }

    private static List<VhdDiskImageFile> OpenVhdChain(string path, int depth, HashSet<string> visited)
    {
        var file = OpenLayer(path, depth, visited, static value => new VhdDiskImageFile(value, FileAccess.Read));
        var layers = new List<VhdDiskImageFile> { file };
        try
        {
            if (file.NeedsParent)
            {
                var parent = ResolveParent(file.GetParentLocations(), Path.GetDirectoryName(path));
                layers.AddRange(OpenVhdChain(parent, depth + 1, visited));
            }

            return layers;
        }
        catch
        {
            DisposeLayers(layers);
            throw;
        }
    }

    private static List<VhdxDiskImageFile> OpenVhdxChain(string path, int depth, HashSet<string> visited)
    {
        var file = OpenLayer(path, depth, visited, static value => new VhdxDiskImageFile(value, FileAccess.Read));
        var layers = new List<VhdxDiskImageFile> { file };
        try
        {
            if (file.NeedsParent)
            {
                var parent = ResolveParent(file.GetParentLocations(), Path.GetDirectoryName(path));
                layers.AddRange(OpenVhdxChain(parent, depth + 1, visited));
            }

            return layers;
        }
        catch
        {
            DisposeLayers(layers);
            throw;
        }
    }

    private static TLayer OpenLayer<TLayer>(string path, int depth, HashSet<string> visited,
        Func<string, TLayer> open)
        where TLayer : IDisposable
    {
        if (depth > MaxParentDepth)
        {
            throw new VhdUnsupportedImageException();
        }

        if (!visited.Add(path))
        {
            throw new VhdDamagedImageException();
        }

        try
        {
            return open(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or InvalidDataException or NotSupportedException)
        {
            throw new VhdDamagedImageException();
        }
    }

    private static VhdImage Create(DiscUtils.VirtualDisk disk, Stream? ownedStream)
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

            if (fileSystems.Count == 0)
            {
                // DiscUtils only reports partitioned volumes; some images hold a file system directly.
                var wholeDisk = TryOpenWholeDiskFileSystem(disk);
                if (wholeDisk is not null)
                {
                    fileSystems.Add(wholeDisk);
                }
            }
        }
        catch
        {
            DisposeFileSystems(fileSystems);
            throw;
        }

        return new VhdImage(disk, ownedStream, BuildVolumes(fileSystems));
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

    private static DiscFileSystem? TryOpenWholeDiskFileSystem(DiscUtils.VirtualDisk disk)
    {
        Func<DiscFileSystem>[] openers =
        [
            () =>
            {
                disk.Content.Position = 0;
                return new FatFileSystem(disk.Content, Ownership.None);
            },
            () =>
            {
                disk.Content.Position = 0;
                return new ExtFileSystem(disk.Content, new FileSystemParameters());
            }
        ];

        foreach (var open in openers)
        {
            try
            {
                return open();
            }
            catch (Exception)
            {
                // Not this file system; the next probe may still match.
            }
        }

        return null;
    }

    private static IReadOnlyList<VhdVolume> BuildVolumes(IReadOnlyList<DiscFileSystem> fileSystems)
    {
        var volumes = new List<VhdVolume>(fileSystems.Count);
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

            volumes.Add(new VhdVolume(fileSystem, name));
        }

        return volumes;
    }

    private static VhdFileEntry? TryReadEntry(VhdVolume volume, string rawPath, int depth)
    {
        if (!DiskImageEntryPath.TryNormalizeFileSystemPath(rawPath, out var normalized))
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
        return new VhdFileEntry(volume.FileSystem, rawPath, fullPath, isDirectory, size,
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

    private static ImageFormat DetectFormat(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return DetectFormat(stream);
    }

    /// <summary>
    /// Recognizes VHDX by its file signature and VHD by the footer cookie. A fixed VHD also stores the
    /// cookie at offset zero, so the leading bytes are checked as well as the trailing footer.
    /// </summary>
    private static ImageFormat DetectFormat(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 8)
        {
            throw new VhdDamagedImageException();
        }

        Span<byte> signature = stackalloc byte[8];
        stream.Position = 0;
        var read = stream.ReadAtLeast(signature, signature.Length, throwOnEndOfStream: false);
        if (read == signature.Length && signature.SequenceEqual("vhdxfile"u8))
        {
            stream.Position = 0;
            return ImageFormat.Vhdx;
        }

        if (stream.Length >= 512)
        {
            stream.Position = stream.Length - 512;
            if (stream.ReadAtLeast(signature, 8, throwOnEndOfStream: false) == 8
                && signature.SequenceEqual("conectix"u8))
            {
                stream.Position = 0;
                return ImageFormat.Vhd;
            }
        }

        if (read == 8 && signature.SequenceEqual("conectix"u8))
        {
            stream.Position = 0;
            return ImageFormat.Vhd;
        }

        stream.Position = 0;
        throw new VhdUnsupportedImageException();
    }

    /// <summary>
    /// Resolves a parent file reference only when it stays inside the directory of the image naming it.
    /// Comparison follows the host file system, so the plugin behaves the same on Windows and Unix.
    /// </summary>
    private static string ResolveParent(IReadOnlyList<string> candidates, string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            throw new VhdMissingParentException();
        }

        var sawExistingOutside = false;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string resolved;
            try
            {
                resolved = Path.GetFullPath(candidate, directory);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException
                                                  or NotSupportedException or System.Security.SecurityException)
            {
                continue;
            }

            if (!File.Exists(resolved))
            {
                continue;
            }

            if (IsInsideDirectory(directory, resolved))
            {
                return resolved;
            }

            sawExistingOutside = true;
        }

        throw sawExistingOutside ? new VhdUnsafeCompanionPathException() : new VhdMissingParentException();
    }

    private static bool IsInsideDirectory(string directory, string resolvedPath)
    {
        var prefix = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return resolvedPath.StartsWith(prefix, PathComparison);
    }

    private static bool IsImagePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && (string.Equals(Path.GetExtension(path), ".vhd", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetExtension(path), ".vhdx", StringComparison.OrdinalIgnoreCase));

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static void DisposeLayers(IEnumerable<IDisposable> layers)
    {
        foreach (var layer in layers)
        {
            try
            {
                layer.Dispose();
            }
            catch (Exception)
            {
                // Best-effort release of a damaged image layer.
            }
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

    private enum ImageFormat
    {
        Vhd,
        Vhdx
    }

    private sealed record VhdVolume(DiscFileSystem FileSystem, string Name);
}
