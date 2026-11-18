using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FluentFTP;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Maps FluentFTP listing items onto the catalog's <see cref="FtpRemoteEntry"/> (#580).
/// The remote listing parser itself is FluentFTP's; only the catalog-facing projection lives here.
/// </summary>
public static class FtpRemoteListingMapper
{
    public static IReadOnlyList<FtpRemoteEntry> Map(IEnumerable<FtpListItem?>? listing, string directoryPath)
    {
        var entries = new List<FtpRemoteEntry>();
        if (listing is null)
        {
            return entries;
        }

        var directory = FtpLocation.NormalizePath(directoryPath);
        foreach (var item in listing)
        {
            if (item is null)
            {
                continue;
            }

            var entry = MapItem(item, directory);
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
    public static FtpRemoteEntry? MapItem(FtpListItem item, string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.SubType is FtpObjectSubType.SelfDirectory or FtpObjectSubType.ParentDirectory)
        {
            return null;
        }

        var directory = FtpLocation.NormalizePath(directoryPath);
        var name = CatalogName(item);
        if (!IsCatalogName(name))
        {
            return null;
        }

        var isDirectory = item.Type == FtpObjectType.Directory;
        var size = isDirectory || item.Type == FtpObjectType.Link ? 0 : Math.Max(0, item.Size);

        return new FtpRemoteEntry(
            name,
            FtpLocation.Combine(directory, name),
            directory,
            isDirectory,
            size,
            Permission(item),
            ModifiedUtc(item));
    }

    /// <summary>Resolves the catalog name of a listing item.</summary>
    internal static string CatalogName(FtpListItem item)
    {
        var name = item.Name;
        if (string.IsNullOrEmpty(name))
        {
            // Some servers answer with an empty name and only fill the full path.
            name = item.FullName ?? string.Empty;
            var slash = name.LastIndexOf('/');
            if (slash >= 0)
            {
                name = name[(slash + 1)..];
            }
        }

        // The handwritten parser stripped a " -> target" suffix from non-link entries; servers
        // still emit it for plain files, so the catalog name must not carry it.
        if (item.Type != FtpObjectType.Link)
        {
            var arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow > 0)
            {
                name = name[..arrow];
            }
        }

        return name.Trim();
    }

    /// <summary>Symbolic permission string in the form the catalog stored before (#580).</summary>
    internal static string? Permission(FtpListItem item)
    {
        var raw = item.RawPermissions;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            // A LIST record carries a leading type character ("drwxr-xr-x"); the catalog keeps
            // only the permission bits. An MLSD "perm" fact is already bare and passes through.
            if (raw.Length == 10 && IsTypeCharacter(raw[0]))
            {
                return raw[1..];
            }

            return raw;
        }

        if (item.Chmod <= 0)
        {
            return null;
        }

        // The octal mode is a number: it is formatted in octal, never through the culture of the
        // machine (which would print "1100101" for 755 on some systems).
        return SymbolicMode(Octal(item.Chmod));
    }

    /// <summary>Formats a permission mode as octal ("755") without consulting the machine culture.</summary>
    internal static string Octal(int mode)
    {
        if (mode <= 0)
        {
            return "0";
        }

        var digits = new StringBuilder();
        var value = mode;
        while (value > 0)
        {
            digits.Insert(0, (char)('0' + (value & 7)));
            value >>= 3;
        }

        return digits.ToString();
    }

    /// <summary>Converts an octal mode (for example "755") to "rwxr-xr-x".</summary>
    public static string SymbolicMode(string mode)
    {
        var digits = mode.Trim();
        if (digits.StartsWith('0') && digits.Length > 3)
        {
            digits = digits[^3..];
        }

        if (digits.Length < 3)
        {
            digits = digits.PadLeft(3, '0');
        }

        digits = digits[^3..];
        var symbols = new char[9];
        for (var i = 0; i < 3; i++)
        {
            var value = digits[i] is >= '0' and <= '7' ? digits[i] - '0' : 0;
            symbols[i * 3] = (value & 4) != 0 ? 'r' : '-';
            symbols[(i * 3) + 1] = (value & 2) != 0 ? 'w' : '-';
            symbols[(i * 3) + 2] = (value & 1) != 0 ? 'x' : '-';
        }

        return new string(symbols);
    }

    /// <summary>The item timestamp normalized to UTC, or null when the server did not report one.</summary>
    internal static DateTime? ModifiedUtc(FtpListItem item)
    {
        if (item.Modified == DateTime.MinValue)
        {
            return null;
        }

        return item.Modified.Kind switch
        {
            DateTimeKind.Utc => item.Modified,
            DateTimeKind.Local => item.Modified.ToUniversalTime(),
            _ => DateTime.SpecifyKind(item.Modified, DateTimeKind.Utc),
        };
    }

    private static bool IsTypeCharacter(char value) =>
        value is '-' or 'd' or 'l' or 'p' or 's' or 'b' or 'c' or 'D';

    private static bool IsCatalogName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name is not "." and not ".." &&
        name.IndexOfAny(['/', '\\', '\0']) < 0;
}
