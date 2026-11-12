using System.Collections.Generic;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Abstraction over directory enumeration so scans can be tested with controlled failures,
/// disconnects and link cycles without requiring privileged real filesystem setups.
/// </summary>
public interface IScanFileSystem
{
    bool DirectoryExists(string path);

    ScanFileSystemEntry GetDirectoryEntry(string path);

    IEnumerable<ScanFileSystemEntry> EnumerateFiles(string path);

    IEnumerable<ScanFileSystemEntry> EnumerateDirectories(string path);
}
