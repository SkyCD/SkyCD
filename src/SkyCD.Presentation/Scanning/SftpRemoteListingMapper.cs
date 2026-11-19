using System;
using System.Collections.Generic;
using System.Text;
using Renci.SshNet.Sftp;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Maps SSH.NET listing items onto the catalog's <see cref="RemoteEntry"/>.
/// Symlinked entries are resolved to their target so the scanner's shared visited-identity
/// logic can treat symlinked directories as cycle candidates.
/// </summary>
public static class SftpRemoteListingMapper
{
    public static IReadOnlyList<RemoteEntry> Map(
        IEnumerable<ISftpFile?>? listing,
        string directoryPath,
        Func<ISftpFile, ISftpFile?>? resolveLinkTarget = null)
    {
        var entries = new List<RemoteEntry>();
        if (listing is null)
        {
            return entries;
        }

        var directory = FtpPath.Normalize(directoryPath);
        foreach (var file in listing)
        {
            if (file is null)
            {
                continue;
            }

            var entry = MapItem(file, directory, resolveLinkTarget);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    /// Projects one listing item. Returns null for the self/parent directory records and for
    /// names the catalog cannot represent (empty, dot entries, or names containing a separator).
    /// </summary>
    public static RemoteEntry? MapItem(
        ISftpFile file,
        string directoryPath,
        Func<ISftpFile, ISftpFile?>? resolveLinkTarget = null)
    {
        ArgumentNullException.ThrowIfNull(file);

        var name = (file.Name ?? string.Empty).Trim();
        if (!IsCatalogName(name))
        {
            return null;
        }

        var directory = FtpPath.Normalize(directoryPath);
        var fullName = FtpPath.Combine(directory, name);
        var visitedIdentity = FtpPath.Normalize(string.IsNullOrWhiteSpace(file.FullName) ? fullName : file.FullName);

        if (file.IsSymbolicLink)
        {
            var target = resolveLinkTarget?.Invoke(file);
            if (target is null)
            {
                // A dangling link stays a file entry that is never descended into.
                return new RemoteEntry(name, fullName, directory, false, 0, Permission(file), ModifiedUtc(file), visitedIdentity);
            }

            visitedIdentity = FtpPath.Normalize(string.IsNullOrWhiteSpace(target.FullName) ? fullName : target.FullName);
            return new RemoteEntry(
                name,
                fullName,
                directory,
                target.IsDirectory,
                target.IsDirectory ? 0 : Math.Max(0, target.Length),
                Permission(target),
                ModifiedUtc(target),
                visitedIdentity);
        }

        var isDirectory = file.IsDirectory;
        var size = isDirectory ? 0 : Math.Max(0, file.Length);
        return new RemoteEntry(name, fullName, directory, isDirectory, size, Permission(file), ModifiedUtc(file), visitedIdentity);
    }

    /// <summary>Symbolic permission string in the form the catalog stored before (#580).</summary>
    internal static string? Permission(ISftpFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var symbols = new char[9];
        symbols[0] = file.OwnerCanRead ? 'r' : '-';
        symbols[1] = file.OwnerCanWrite ? 'w' : '-';
        symbols[2] = file.OwnerCanExecute ? 'x' : '-';
        symbols[3] = file.GroupCanRead ? 'r' : '-';
        symbols[4] = file.GroupCanWrite ? 'w' : '-';
        symbols[5] = file.GroupCanExecute ? 'x' : '-';
        symbols[6] = file.OthersCanRead ? 'r' : '-';
        symbols[7] = file.OthersCanWrite ? 'w' : '-';
        symbols[8] = file.OthersCanExecute ? 'x' : '-';
        return new StringBuilder(9).Append(symbols).ToString();
    }

    /// <summary>The item timestamp normalized to UTC, or null when the server did not report one.</summary>
    internal static DateTime? ModifiedUtc(ISftpFile file)
    {
        var modified = file.LastWriteTimeUtc;
        if (modified == DateTime.MinValue)
        {
            return null;
        }

        return modified.Kind switch
        {
            DateTimeKind.Utc => modified,
            DateTimeKind.Local => modified.ToUniversalTime(),
            _ => DateTime.SpecifyKind(modified, DateTimeKind.Utc),
        };
    }

    private static bool IsCatalogName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name is not "." and not ".." &&
        name.IndexOfAny(['/', '\\', '\0']) < 0;
}
