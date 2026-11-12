using System;
using System.IO;

namespace SkyCD.Presentation.Scanning;

public sealed record ScanFileSystemEntry(
    string FullPath,
    string Name,
    bool IsDirectory,
    long Length,
    FileAttributes Attributes,
    DateTime CreationTime,
    DateTime LastAccessTime,
    DateTime LastWriteTime,
    bool IsLinkOrReparsePoint,
    string VisitedIdentity,
    bool IsReadOnly = false);
