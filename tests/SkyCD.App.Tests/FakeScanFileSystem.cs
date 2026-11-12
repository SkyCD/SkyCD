using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Presentation.Scanning;

namespace SkyCD.App.Tests;

internal sealed class FakeScanFileSystem : IScanFileSystem
{
    private readonly Dictionary<string, Node> nodes;
    private readonly StringComparer comparer;

    public FakeScanFileSystem(StringComparer? comparer = null)
    {
        this.comparer = comparer ?? (OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        nodes = new Dictionary<string, Node>(this.comparer);
    }

    public int EnumerateFilesCallCount { get; private set; }

    public int EnumerateDirectoriesCallCount { get; private set; }

    public void AddDirectory(
        string path,
        FileAttributes attributes = FileAttributes.Directory,
        bool isLink = false,
        string? visitedIdentity = null)
    {
        var normalized = Normalize(path);
        var existingChildren = nodes.TryGetValue(normalized, out var existing)
            ? existing.Children
            : new List<string>();

        nodes[normalized] = new Node(
            Entry: new ScanFileSystemEntry(
                FullPath: normalized,
                Name: GetEntryName(normalized),
                IsDirectory: true,
                Length: 0,
                Attributes: attributes | FileAttributes.Directory | (isLink ? FileAttributes.ReparsePoint : 0),
                CreationTime: DateTime.UtcNow,
                LastAccessTime: DateTime.UtcNow,
                LastWriteTime: DateTime.UtcNow,
                IsLinkOrReparsePoint: isLink,
                VisitedIdentity: visitedIdentity ?? normalized),
            Children: existingChildren,
            EnumerateExceptionFactory: null,
            FailTimesRemaining: 0);

        AttachToParent(normalized);
    }

    public void AddFile(
        string path,
        long length = 1,
        FileAttributes attributes = FileAttributes.Normal,
        bool isLink = false)
    {
        var normalized = Normalize(path);
        EnsureDirectory(Path.GetDirectoryName(normalized) ?? normalized);

        var entry = new ScanFileSystemEntry(
            FullPath: normalized,
            Name: GetEntryName(normalized),
            IsDirectory: false,
            Length: length,
            Attributes: attributes | (isLink ? FileAttributes.ReparsePoint : 0),
            CreationTime: DateTime.UtcNow,
            LastAccessTime: DateTime.UtcNow,
            LastWriteTime: DateTime.UtcNow,
            IsLinkOrReparsePoint: isLink,
            VisitedIdentity: normalized);

        nodes[normalized] = new Node(entry, Children: [], EnumerateExceptionFactory: null, FailTimesRemaining: 0);
        AttachToParent(normalized);
    }

    public void LinkDirectory(string linkPath, string targetPath)
    {
        var normalizedLink = Normalize(linkPath);
        var normalizedTarget = Normalize(targetPath);
        EnsureDirectory(normalizedTarget);

        nodes[normalizedLink] = new Node(
            Entry: new ScanFileSystemEntry(
                FullPath: normalizedLink,
                Name: GetEntryName(normalizedLink),
                IsDirectory: true,
                Length: 0,
                Attributes: FileAttributes.Directory | FileAttributes.ReparsePoint,
                CreationTime: DateTime.UtcNow,
                LastAccessTime: DateTime.UtcNow,
                LastWriteTime: DateTime.UtcNow,
                IsLinkOrReparsePoint: true,
                VisitedIdentity: normalizedTarget),
            Children: nodes[normalizedTarget].Children.ToList(),
            EnumerateExceptionFactory: null,
            FailTimesRemaining: 0);

        AttachToParent(normalizedLink);
    }

    public void SetEnumerateFailure(string path, Func<Exception> exceptionFactory, int failTimes)
    {
        var normalized = Normalize(path);
        EnsureDirectory(normalized);
        var existing = nodes[normalized];
        nodes[normalized] = existing with
        {
            EnumerateExceptionFactory = exceptionFactory,
            FailTimesRemaining = failTimes
        };
    }

    public bool DirectoryExists(string path) => nodes.TryGetValue(Normalize(path), out var node) && node.Entry.IsDirectory;

    public ScanFileSystemEntry GetDirectoryEntry(string path)
    {
        var normalized = Normalize(path);
        if (!nodes.TryGetValue(normalized, out var node) || !node.Entry.IsDirectory)
        {
            throw new DirectoryNotFoundException(normalized);
        }

        return node.Entry;
    }

    public IEnumerable<ScanFileSystemEntry> EnumerateFiles(string path)
    {
        EnumerateFilesCallCount++;
        foreach (var child in EnumerateChildren(path, directories: false))
        {
            yield return child;
        }
    }

    public IEnumerable<ScanFileSystemEntry> EnumerateDirectories(string path)
    {
        EnumerateDirectoriesCallCount++;
        foreach (var child in EnumerateChildren(path, directories: true))
        {
            yield return child;
        }
    }

    private IEnumerable<ScanFileSystemEntry> EnumerateChildren(string path, bool directories)
    {
        var normalized = Normalize(path);
        if (!nodes.TryGetValue(normalized, out var node) || !node.Entry.IsDirectory)
        {
            throw new DirectoryNotFoundException(normalized);
        }

        if (node.FailTimesRemaining > 0 && node.EnumerateExceptionFactory is not null)
        {
            nodes[normalized] = node with { FailTimesRemaining = node.FailTimesRemaining - 1 };
            throw node.EnumerateExceptionFactory();
        }

        foreach (var childPath in node.Children)
        {
            if (!nodes.TryGetValue(childPath, out var child))
            {
                continue;
            }

            if (child.Entry.IsDirectory == directories)
            {
                yield return child.Entry;
            }
        }
    }

    private void EnsureDirectory(string path)
    {
        var normalized = Normalize(path);
        if (nodes.ContainsKey(normalized))
        {
            return;
        }

        if (IsFilesystemRoot(normalized))
        {
            nodes[normalized] = new Node(
                Entry: new ScanFileSystemEntry(
                    FullPath: normalized,
                    Name: GetEntryName(normalized),
                    IsDirectory: true,
                    Length: 0,
                    Attributes: FileAttributes.Directory,
                    CreationTime: DateTime.UtcNow,
                    LastAccessTime: DateTime.UtcNow,
                    LastWriteTime: DateTime.UtcNow,
                    IsLinkOrReparsePoint: false,
                    VisitedIdentity: normalized),
                Children: [],
                EnumerateExceptionFactory: null,
                FailTimesRemaining: 0);
            return;
        }

        AddDirectory(normalized);
    }

    private void AttachToParent(string normalizedPath)
    {
        if (IsFilesystemRoot(normalizedPath))
        {
            return;
        }

        var parent = Path.GetDirectoryName(normalizedPath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            return;
        }

        var normalizedParent = Normalize(parent);
        if (comparer.Equals(normalizedParent, normalizedPath))
        {
            return;
        }

        EnsureDirectory(normalizedParent);
        if (!nodes.TryGetValue(normalizedParent, out var parentNode))
        {
            return;
        }

        if (!parentNode.Children.Contains(normalizedPath, comparer))
        {
            parentNode.Children.Add(normalizedPath);
        }
    }

    private static bool IsFilesystemRoot(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(trimmed) || trimmed == Path.DirectorySeparatorChar.ToString())
        {
            return true;
        }

        // Windows drive root: "C:" (GetFullPath("C:") would otherwise expand to the cwd).
        return trimmed.Length == 2 && char.IsLetter(trimmed[0]) && trimmed[1] == ':';
    }

    private static string GetEntryName(string normalizedPath)
    {
        var trimmed = normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }

    private static string Normalize(string path)
    {
        var unified = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var trimmed = unified.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (IsFilesystemRoot(trimmed))
        {
            return trimmed;
        }

        return Path.GetFullPath(unified).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed record Node(
        ScanFileSystemEntry Entry,
        List<string> Children,
        Func<Exception>? EnumerateExceptionFactory,
        int FailTimesRemaining);
}
