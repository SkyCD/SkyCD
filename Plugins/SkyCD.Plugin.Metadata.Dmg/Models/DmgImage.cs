using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DiscUtils;
using DiscUtils.Ext;
using DiscUtils.Fat;
using DiscUtils.HfsPlus;
using DiscUtils.Setup;
using DiscUtils.Streams;
using DmgDisk = DiscUtils.Dmg.Disk;
using SkyCD.Plugin.Metadata.Dmg.Exceptions;
using SkyCD.Plugin.Metadata.Dmg.Parsing;

namespace SkyCD.Plugin.Metadata.Dmg.Models;

/// <summary>One file or folder reported by a file system inside a DMG image.</summary>
internal sealed record DmgFileEntry(
    DiscFileSystem FileSystem,
    string RawPath,
    string FullPath,
    bool IsDirectory,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc);

internal sealed record DmgVolume(DiscFileSystem FileSystem, string Name);

/// <summary>
/// An opened DMG (UDIF) image with the supported file systems found on its partitions. The UDIF
/// container is decoded by DiscUtils, which handles raw, zlib, ADC, bzip2, and LZFSE blocks, and every
/// file system stays open until the image is disposed so contained files can be read one at a time.
/// </summary>
internal sealed class DmgImage : IDisposable
{
    private const int CopyBufferBytes = 81_920;

    private readonly DmgDisk? disk;
    private readonly Stream content;
    private readonly bool ownsContent;
    private readonly IReadOnlyList<DmgVolume> volumes;
    private bool disposed;

    static DmgImage()
    {
        SetupHelper.RegisterAssembly(typeof(FatFileSystem).Assembly);
        SetupHelper.RegisterAssembly(typeof(ExtFileSystem).Assembly);
        SetupHelper.RegisterAssembly(typeof(HfsPlusFileSystem).Assembly);
    }

    private DmgImage(DmgDisk? disk, Stream content, bool ownsContent, IReadOnlyList<DmgVolume> volumes)
    {
        this.disk = disk;
        this.content = content;
        this.ownsContent = ownsContent;
        this.volumes = volumes;
    }

    /// <summary>Gets whether at least one supported file system was found in the image.</summary>
    public bool HasFileSystems => volumes.Count > 0;

    /// <summary>Opens a local image file.</summary>
    public static DmgImage OpenFromPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var content = new FileStream(Path.GetFullPath(path), FileMode.Open, FileAccess.Read, FileShare.Read,
            CopyBufferBytes);
        try
        {
            return Create(content, ownsContent: true);
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
    /// </summary>
    public static DmgImage Open(Stream source, string? sourcePath)
    {
        if (IsDmgPath(sourcePath) && File.Exists(sourcePath))
        {
            return OpenFromPath(sourcePath!);
        }

        ArgumentNullException.ThrowIfNull(source);
        if (source.CanSeek)
        {
            return OpenFromStream(source, ownsStream: false);
        }

        var temporaryPath = Path.Combine(Path.GetTempPath(), $"skycd-dmg-source-{Guid.NewGuid():N}.dmg");
        var copy = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            CopyBufferBytes, FileOptions.DeleteOnClose);
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

    /// <summary>Opens an image from a seekable stream that the image does not dispose when not owned.</summary>
    internal static DmgImage OpenFromStream(Stream stream, bool ownsStream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("The DMG image stream must be seekable.", nameof(stream));
        }

        return Create(stream, ownsStream);
    }

    /// <summary>Opens a contained file for reading directly from the image.</summary>
    public Stream OpenEntry(DmgFileEntry entry)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return entry.FileSystem.OpenFile(entry.RawPath, FileMode.Open, FileAccess.Read);
    }

    /// <summary>
    /// Enumerates the files and folders of every readable volume. Unsafe, damaged, reparse-point and
    /// oversized-depth entries are skipped, and the configured entry limit bounds the result.
    /// </summary>
    public IEnumerable<DmgFileEntry> EnumerateEntries(DmgIndexLimits limits, CancellationToken cancellationToken)
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
            disk?.Dispose();
        }
        catch (Exception)
        {
            // Best-effort release of a damaged disk.
        }

        if (ownsContent)
        {
            try
            {
                content.Dispose();
            }
            catch (Exception)
            {
                // Best-effort release of a damaged image stream.
            }
        }
    }

    private static DmgImage Create(Stream content, bool ownsContent)
    {
        DmgHeader.Validate(content);

        DmgDisk disk;
        try
        {
            disk = new DmgDisk(content, Ownership.None);
        }
        catch (Exception exception) when (IsImageDamage(exception))
        {
            throw new DmgDamagedImageException();
        }

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
                // DiscUtils only reports partitioned volumes; many DMGs hold a file system directly.
                var wholeDisk = TryOpenWholeDiskFileSystem(disk.Content);
                if (wholeDisk is not null)
                {
                    fileSystems.Add(wholeDisk);
                }
            }
        }
        catch (Exception exception) when (IsImageDamage(exception))
        {
            DisposeFileSystems(fileSystems);
            disk.Dispose();
            if (ownsContent)
            {
                content.Dispose();
            }

            throw new DmgDamagedImageException();
        }
        catch
        {
            DisposeFileSystems(fileSystems);
            disk.Dispose();
            throw;
        }

        return new DmgImage(disk, content, ownsContent, BuildVolumes(fileSystems));
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
            () => new HfsPlusFileSystem(content),
            () => new FatFileSystem(content, Ownership.None),
            () => new ExtFileSystem(content, new FileSystemParameters())
        ];

        foreach (var open in openers)
        {
            try
            {
                content.Position = 0;
                var fileSystem = open();
                // Probing the root makes a misdetected file system fail here rather than during indexing.
                fileSystem.GetFileSystemEntries(string.Empty);
                content.Position = 0;
                return fileSystem;
            }
            catch (Exception)
            {
                // Not this file system; the next probe may still match.
            }
        }

        return null;
    }

    private static IReadOnlyList<DmgVolume> BuildVolumes(IReadOnlyList<DiscFileSystem> fileSystems)
    {
        var volumes = new List<DmgVolume>(fileSystems.Count);
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

            volumes.Add(new DmgVolume(fileSystem, name));
        }

        return volumes;
    }

    private static DmgFileEntry? TryReadEntry(DmgVolume volume, string rawPath, int depth)
    {
        if (!DmgEntryPath.TryNormalize(rawPath, out var normalized))
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
        return new DmgFileEntry(volume.FileSystem, rawPath, fullPath, isDirectory, size,
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

    private static bool IsImageDamage(Exception exception) =>
        exception is IOException or InvalidDataException or NotSupportedException or ArgumentException
            or NotImplementedException or IndexOutOfRangeException or FormatException or OverflowException;

    private static bool IsDmgPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetExtension(path), ".dmg", StringComparison.OrdinalIgnoreCase);

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
}
