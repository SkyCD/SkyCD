using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SkyCD.Presentation.Scanning;

public static class FtpListingParser
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly Regex UnixListing = new(
        @"^(?<perm>[-dlpsbcDLPSBC][rwxstST-]{9})\s+\d+\s+\S+\s+\S+\s+(?<size>\d+)\s+(?<month>[A-Za-z]{3})\s+(?<day>\d{1,2})\s+(?<date>\d{1,2}:\d{2}|\d{4})\s+(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WindowsListing = new(
        @"^(?<month>\d{2})-(?<day>\d{2})-(?<year>\d{2})\s+(?<hour>\d{1,2}):(?<minute>\d{2})(?<ampm>AM|PM)\s+(?:(?<dir><DIR>)|(?<size>\d+))\s+(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<FtpRemoteEntry> Parse(string? listing, string directoryPath)
    {
        var entries = new List<FtpRemoteEntry>();
        if (string.IsNullOrWhiteSpace(listing))
        {
            return entries;
        }

        var directory = FtpLocation.NormalizePath(directoryPath);
        foreach (var raw in listing.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("total ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FtpRemoteEntry? entry = null;
            if (LooksLikeMlsd(line))
            {
                entry = ParseMlsd(line, directory);
            }
            else if (TryParseUnix(line, directory, out var unix))
            {
                entry = unix;
            }
            else if (TryParseWindows(line, directory, out var windows))
            {
                entry = windows;
            }

            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    internal static string SymbolicMode(string mode)
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

    private static bool LooksLikeMlsd(string line)
    {
        var separator = line.IndexOf(' ');
        return separator > 0 && line.AsSpan(0, separator).Contains('=');
    }

    private static FtpRemoteEntry? ParseMlsd(string line, string directory)
    {
        var separator = line.IndexOf(' ');
        if (separator < 0)
        {
            return null;
        }

        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in line[..separator].Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = fact.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            facts[fact[..equals]] = fact[(equals + 1)..];
        }

        if (!facts.TryGetValue("type", out var type) ||
            type.Equals("cdir", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("pdir", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = line[(separator + 1)..].Trim();
        if (!IsCatalogName(name))
        {
            return null;
        }

        var isDirectory = type.Equals("dir", StringComparison.OrdinalIgnoreCase);
        long size = 0;
        if (!isDirectory && facts.TryGetValue("size", out var sizeText))
        {
            long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
        }

        string? permission = null;
        if (facts.TryGetValue("unix.mode", out var mode) && mode.Length > 0)
        {
            permission = SymbolicMode(mode);
        }
        else if (facts.TryGetValue("perm", out var perm) && perm.Length > 0)
        {
            permission = perm;
        }

        return Create(name, directory, isDirectory, size, permission, ParseMlsdTime(facts));
    }

    private static DateTime? ParseMlsdTime(Dictionary<string, string> facts)
    {
        if (!facts.TryGetValue("modify", out var modify) || modify.Length < 14)
        {
            return null;
        }

        return DateTime.TryParseExact(
            modify[..14],
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var modified)
            ? modified
            : null;
    }

    private static bool TryParseUnix(string line, string directory, out FtpRemoteEntry? entry)
    {
        entry = null;
        var match = UnixListing.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var name = LinkName(match.Groups["name"].Value.Trim());
        if (!IsCatalogName(name))
        {
            return false;
        }

        var permissionToken = match.Groups["perm"].Value;
        var isDirectory = permissionToken[0] is 'd' or 'D';
        long.TryParse(match.Groups["size"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
        entry = Create(
            name,
            directory,
            isDirectory,
            isDirectory ? 0 : size,
            permissionToken.Length >= 10 ? permissionToken[1..] : permissionToken,
            ParseUnixTime(match.Groups["month"].Value, match.Groups["day"].Value, match.Groups["date"].Value));
        return true;
    }

    private static bool TryParseWindows(string line, string directory, out FtpRemoteEntry? entry)
    {
        entry = null;
        var match = WindowsListing.Match(line);
        if (!match.Success)
        {
            return false;
        }

        var name = match.Groups["name"].Value.Trim();
        if (!IsCatalogName(name))
        {
            return false;
        }

        var isDirectory = match.Groups["dir"].Success;
        long size = 0;
        if (!isDirectory)
        {
            long.TryParse(match.Groups["size"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out size);
        }

        entry = Create(name, directory, isDirectory, size, permission: null, ParseWindowsTime(match));
        return true;
    }

    private static DateTime? ParseUnixTime(string month, string dayText, string timeOrYear)
    {
        var monthIndex = Array.FindIndex(Months, name => name.Equals(month, StringComparison.OrdinalIgnoreCase));
        if (monthIndex < 0 || !int.TryParse(dayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day))
        {
            return null;
        }

        if (timeOrYear.Contains(':'))
        {
            var parts = timeOrYear.Split(':');
            if (parts.Length < 2 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) ||
                !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute))
            {
                return null;
            }

            var stamp = new DateTime(DateTime.UtcNow.Year, monthIndex + 1, day, hour, minute, 0, DateTimeKind.Utc);
            if (stamp > DateTime.UtcNow.AddDays(2))
            {
                stamp = stamp.AddYears(-1);
            }

            return stamp;
        }

        return int.TryParse(timeOrYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? new DateTime(year, monthIndex + 1, day, 0, 0, 0, DateTimeKind.Utc)
            : null;
    }

    private static DateTime? ParseWindowsTime(Match match)
    {
        if (!int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) ||
            !int.TryParse(match.Groups["year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(match.Groups["hour"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) ||
            !int.TryParse(match.Groups["minute"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute))
        {
            return null;
        }

        year += year >= 80 ? 1900 : 2000;
        if (match.Groups["ampm"].Value.Equals("PM", StringComparison.OrdinalIgnoreCase) && hour < 12)
        {
            hour += 12;
        }

        if (match.Groups["ampm"].Value.Equals("AM", StringComparison.OrdinalIgnoreCase) && hour == 12)
        {
            hour = 0;
        }

        try
        {
            return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string LinkName(string name)
    {
        var arrow = name.IndexOf(" -> ", StringComparison.Ordinal);
        return arrow > 0 ? name[..arrow].Trim() : name;
    }

    private static bool IsCatalogName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name is not "." and not ".." &&
        name.IndexOfAny(['/', '\\', '\0']) < 0;

    private static FtpRemoteEntry Create(
        string name,
        string directory,
        bool isDirectory,
        long size,
        string? permission,
        DateTime? modifiedUtc) =>
        new(
            name,
            FtpLocation.Combine(directory, name),
            directory,
            isDirectory,
            size,
            permission,
            modifiedUtc);
}
