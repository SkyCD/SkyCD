using System;
using System.Collections.Generic;
using System.IO;

namespace SkyCD.Presentation.Scanning;

public sealed class RealScanFileSystem : IScanFileSystem
{
    public bool DirectoryExists(string path) =>
        !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    public ScanFileSystemEntry GetDirectoryEntry(string path)
    {
        var info = new DirectoryInfo(path);
        return ToEntry(info, isDirectory: true, length: 0);
    }

    public IEnumerable<ScanFileSystemEntry> EnumerateFiles(string path)
    {
        foreach (var file in new DirectoryInfo(path).EnumerateFiles())
        {
            yield return ToEntry(file, isDirectory: false, length: SafeLength(file));
        }
    }

    public IEnumerable<ScanFileSystemEntry> EnumerateDirectories(string path)
    {
        foreach (var directory in new DirectoryInfo(path).EnumerateDirectories())
        {
            yield return ToEntry(directory, isDirectory: true, length: 0);
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static ScanFileSystemEntry ToEntry(FileSystemInfo info, bool isDirectory, long length)
    {
        FileAttributes attributes;
        try
        {
            attributes = info.Attributes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            attributes = FileAttributes.Normal;
        }

        var isLink = (attributes & FileAttributes.ReparsePoint) != 0;
        return new ScanFileSystemEntry(
            FullPath: info.FullName,
            Name: info.Name,
            IsDirectory: isDirectory,
            Length: length,
            Attributes: attributes,
            CreationTime: SafeTime(() => info.CreationTime),
            LastAccessTime: SafeTime(() => info.LastAccessTime),
            LastWriteTime: SafeTime(() => info.LastWriteTime),
            IsLinkOrReparsePoint: isLink,
            VisitedIdentity: ResolveVisitedIdentity(info, isLink),
            IsReadOnly: !isDirectory && info is FileInfo fileInfo && fileInfo.IsReadOnly);
    }

    private static DateTime SafeTime(Func<DateTime> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    public static string ResolveVisitedIdentity(FileSystemInfo info, bool isLinkOrReparsePoint)
    {
        if (isLinkOrReparsePoint)
        {
            try
            {
                var target = info.ResolveLinkTarget(returnFinalTarget: true);
                if (target is not null && !string.IsNullOrWhiteSpace(target.FullName))
                {
                    return NormalizeIdentity(target.FullName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Fall back to the link path itself.
            }
        }

        return NormalizeIdentity(info.FullName);
    }

    public static string NormalizeIdentity(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
