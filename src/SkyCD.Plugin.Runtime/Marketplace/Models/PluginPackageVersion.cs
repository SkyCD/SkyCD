using System;
using System.Text;
using SkyCD.Plugin.Runtime.Marketplace.Exceptions;

namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>
/// A NuGet package version as it appears in a v3 package content version list and in publisher manifests.
/// Parsing and ordering follow NuGet's normalized version rules: four dotted numeric segments, SemVer 2
/// prerelease labels ordered dot-segment by dot-segment (numeric labels compare numerically and sort before
/// alphanumeric ones), and build metadata preserved for display but ignored for ordering and identity.
/// </summary>
public sealed class PluginPackageVersion : IComparable<PluginPackageVersion>, IEquatable<PluginPackageVersion>
{
    private readonly int[] numbers;
    private readonly string[] releaseLabels;
    private readonly string normalized;

    private PluginPackageVersion(int[] numbers, string[] releaseLabels, string? metadata, string original)
    {
        this.numbers = numbers;
        this.releaseLabels = releaseLabels;
        Metadata = metadata;
        Original = original;

        var builder = new StringBuilder();
        builder.Append(numbers[0]).Append('.').Append(numbers[1]).Append('.').Append(numbers[2]);
        if (numbers[3] != 0)
        {
            builder.Append('.').Append(numbers[3]);
        }

        if (releaseLabels.Length > 0)
        {
            builder.Append('-').Append(string.Join('.', releaseLabels));
        }

        normalized = builder.ToString();
    }

    /// <summary>The version text exactly as it was supplied.</summary>
    public string Original { get; }

    /// <summary>NuGet normalized version without build metadata; use it as the cache and comparison key.</summary>
    public string Normalized => normalized;

    /// <summary>SemVer build metadata, or <see langword="null"/>. Ignored for ordering and identity.</summary>
    public string? Metadata { get; }

    public bool IsPrerelease => releaseLabels.Length > 0;

    public int Major => numbers[0];

    public int Minor => numbers[1];

    public int Patch => numbers[2];

    /// <summary>The major.minor.patch part, suitable for comparing against a host version.</summary>
    public Version ToVersion()
    {
        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    public static bool TryParse(string? text, out PluginPackageVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        var numbers = new int[4];
        var segment = 0;
        var index = 0;

        while (index < value.Length && !IsTerminator(value[index]))
        {
            if (segment > 3)
            {
                return false;
            }

            long current = 0;
            var digits = 0;
            for (; index < value.Length; index++)
            {
                var character = value[index];
                if (IsTerminator(character))
                {
                    break;
                }

                if (character is < '0' or > '9')
                {
                    return false;
                }

                digits++;
                current = current * 10 + (character - '0');
                if (current > int.MaxValue)
                {
                    return false;
                }
            }

            if (digits == 0)
            {
                return false;
            }

            numbers[segment] = (int)current;

            if (index < value.Length && value[index] == '.')
            {
                segment++;
                if (segment > 3)
                {
                    return false;
                }

                index++;
                continue;
            }

            break;
        }

        string[] releaseLabels = [];
        string? metadata = null;

        if (index < value.Length && value[index] == '-')
        {
            index++;
            var start = index;
            for (; index < value.Length; index++)
            {
                var character = value[index];
                if (character == '+')
                {
                    break;
                }

                if (!IsLabelCharacter(character))
                {
                    return false;
                }
            }

            var release = value[start..index];
            if (release.Length == 0)
            {
                return false;
            }

            releaseLabels = release.Split('.');
            foreach (var label in releaseLabels)
            {
                if (label.Length == 0)
                {
                    return false;
                }
            }
        }

        if (index < value.Length && value[index] == '+')
        {
            index++;
            var start = index;
            for (; index < value.Length; index++)
            {
                if (!IsLabelCharacter(value[index]))
                {
                    return false;
                }
            }

            if (index == start)
            {
                return false;
            }

            metadata = value[start..index];
        }

        if (index != value.Length)
        {
            return false;
        }

        version = new PluginPackageVersion(numbers, releaseLabels, metadata, value);
        return true;

        static bool IsTerminator(char character)
        {
            return character is '-' or '+' or '.';
        }

        static bool IsIdentifierCharacter(char character)
        {
            return character is >= '0' and <= '9' or >= 'a' and <= 'z' or >= 'A' and <= 'Z' or '-';
        }

        static bool IsLabelCharacter(char character)
        {
            return character == '.' || IsIdentifierCharacter(character);
        }
    }

    /// <summary>Parses a version or throws <see cref="InvalidPluginPackageVersionException"/>.</summary>
    public static PluginPackageVersion Parse(string? text)
    {
        if (!TryParse(text, out var version))
        {
            throw new InvalidPluginPackageVersionException(text ?? string.Empty);
        }

        return version!;
    }

    public int CompareTo(PluginPackageVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        for (var segment = 0; segment < 4; segment++)
        {
            var comparison = numbers[segment].CompareTo(other.numbers[segment]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        if (releaseLabels.Length == 0 || other.releaseLabels.Length == 0)
        {
            return other.releaseLabels.Length.CompareTo(releaseLabels.Length);
        }

        var shared = Math.Min(releaseLabels.Length, other.releaseLabels.Length);
        for (var label = 0; label < shared; label++)
        {
            var comparison = CompareLabels(releaseLabels[label], other.releaseLabels[label]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return releaseLabels.Length.CompareTo(other.releaseLabels.Length);
    }

    public bool Equals(PluginPackageVersion? other)
    {
        return other is not null && CompareTo(other) == 0;
    }

    public override bool Equals(object? obj)
    {
        return obj is PluginPackageVersion other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var number in numbers)
        {
            hash.Add(number);
        }

        foreach (var label in releaseLabels)
        {
            hash.Add(label, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        return Original;
    }

    public static bool operator <(PluginPackageVersion? left, PluginPackageVersion? right)
    {
        return left is null ? right is not null : left.CompareTo(right) < 0;
    }

    public static bool operator >(PluginPackageVersion? left, PluginPackageVersion? right)
    {
        return left is not null && left.CompareTo(right) > 0;
    }

    public static bool operator <=(PluginPackageVersion? left, PluginPackageVersion? right)
    {
        return left is null || left.CompareTo(right) <= 0;
    }

    public static bool operator >=(PluginPackageVersion? left, PluginPackageVersion? right)
    {
        return left is null ? right is null : left.CompareTo(right) >= 0;
    }

    public static bool operator ==(PluginPackageVersion? left, PluginPackageVersion? right)
    {
        return left is null ? right is null : left.Equals(right);
    }

    public static bool operator !=(PluginPackageVersion? left, PluginPackageVersion? right)
    {
        return !(left == right);
    }

    private static int CompareLabels(string left, string right)
    {
        var leftNumeric = IsNumeric(left);
        var rightNumeric = IsNumeric(right);

        if (leftNumeric && rightNumeric)
        {
            var trimmedLeft = left.TrimStart('0');
            var trimmedRight = right.TrimStart('0');
            var comparison = trimmedLeft.Length.CompareTo(trimmedRight.Length);
            return comparison != 0
                ? comparison
                : string.CompareOrdinal(trimmedLeft, trimmedRight);
        }

        if (leftNumeric)
        {
            return -1;
        }

        if (rightNumeric)
        {
            return 1;
        }

        return string.CompareOrdinal(left, right);
    }

    private static bool IsNumeric(string label)
    {
        foreach (var character in label)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return label.Length > 0;
    }
}
