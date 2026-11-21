using System;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// NuGet identity normalization and version ordering shared by the installer and the plugin layout.
/// </summary>
public static class PackageIdentity
{
    /// <summary>Trims a NuGet id or version and removes SemVer build metadata (<c>+metadata</c>).</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var plus = trimmed.IndexOf('+');
        return plus >= 0 ? trimmed[..plus] : trimmed;
    }

    /// <summary>Whether the version is an exact version: <c>X</c> to <c>X.Y.Z.W</c> with 1-5 numeric segments, optional prerelease and build metadata.</summary>
    public static bool IsExactVersion(string? value)
    {
        if (!TrySplitVersion(Normalize(value), out var numeric, out var prerelease))
        {
            return false;
        }

        if (numeric.Length is < 1 or > 5)
        {
            return false;
        }

        foreach (var segment in numeric)
        {
            if (segment.Length == 0 || !IsDigits(segment))
            {
                return false;
            }
        }

        return prerelease is null || IsValidPrerelease(prerelease);
    }

    /// <summary>
    /// Compares two version strings using NuGet ordering: missing numeric segments count as zero,
    /// release sorts after prerelease, and prerelease identifiers compare numerically or ordinal.
    /// </summary>
    public static int CompareVersions(string? left, string? right)
    {
        Split(left, out var leftNumeric, out var leftPrerelease);
        Split(right, out var rightNumeric, out var rightPrerelease);

        var length = Math.Max(leftNumeric.Length, rightNumeric.Length);
        for (var index = 0; index < length; index++)
        {
            var comparison = Segment(leftNumeric, index).CompareTo(Segment(rightNumeric, index));
            if (comparison != 0)
            {
                return comparison;
            }
        }

        if (leftPrerelease is null && rightPrerelease is null)
        {
            return 0;
        }

        if (leftPrerelease is null)
        {
            return 1;
        }

        if (rightPrerelease is null)
        {
            return -1;
        }

        return ComparePrerelease(leftPrerelease, rightPrerelease);
    }

    /// <summary>Converts a package version to a dotted numeric assembly version; prerelease identifiers are ignored.</summary>
    public static Version ToAssemblyVersion(string? value)
    {
        Split(value, out var numeric, out _);
        var major = Segment(numeric, 0);
        var minor = Segment(numeric, 1);
        var build = Segment(numeric, 2);
        var revision = Segment(numeric, 3);
        return new Version((int)Math.Min(major, int.MaxValue), (int)Math.Min(minor, int.MaxValue),
            (int)Math.Min(build, int.MaxValue), (int)Math.Min(revision, int.MaxValue));
    }

    private static void Split(string? value, out long[] numeric, out string? prerelease)
    {
        if (!TrySplitVersion(Normalize(value), out var segments, out prerelease))
        {
            numeric = [0];
            prerelease = null;
            return;
        }

        numeric = new long[segments.Length];
        for (var index = 0; index < segments.Length; index++)
        {
            numeric[index] = long.TryParse(segments[index], out var parsed) ? parsed : 0;
        }
    }

    private static long Segment(long[] values, int index) =>
        index < values.Length ? values[index] : 0;

    private static bool TrySplitVersion(string value, out string[] numeric, out string? prerelease)
    {
        numeric = [];
        prerelease = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var core = value;
        var prereleaseIndex = core.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            prerelease = core[(prereleaseIndex + 1)..];
            core = core[..prereleaseIndex];
        }

        numeric = core.Split('.');
        return numeric.Length > 0;
    }

    private static bool IsDigits(string value)
    {
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidPrerelease(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0)
            {
                return false;
            }

            foreach (var character in identifier)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int ComparePrerelease(string left, string right)
    {
        var leftIdentifiers = left.Split('.');
        var rightIdentifiers = right.Split('.');
        var length = Math.Min(leftIdentifiers.Length, rightIdentifiers.Length);

        for (var index = 0; index < length; index++)
        {
            var comparison = CompareIdentifier(leftIdentifiers[index], rightIdentifiers[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return leftIdentifiers.Length.CompareTo(rightIdentifiers.Length);
    }

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = IsDigits(left);
        var rightNumeric = IsDigits(right);

        if (leftNumeric && rightNumeric)
        {
            var leftValue = left.TrimStart('0');
            var rightValue = right.TrimStart('0');
            if (leftValue.Length != rightValue.Length)
            {
                return leftValue.Length.CompareTo(rightValue.Length);
            }

            return string.CompareOrdinal(leftValue, rightValue);
        }

        if (leftNumeric)
        {
            return -1;
        }

        if (rightNumeric)
        {
            return 1;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
