using System;

namespace SkyCD.Plugin.Runtime.Discovery;

/// <summary>
/// Evaluates whether a plugin is compatible with the current host version (issue #1228).
/// This is the one place that turns raw version bounds into a verdict: the manager, the
/// catalog rules and the installer all delegate here instead of parsing versions themselves.
/// </summary>
public static class PluginCompatibilityEvaluator
{
    /// <summary>Host default applied when a plugin declares no minimum (legacy single-version plugins).</summary>
    public static readonly Version DefaultMinHostVersion = new(3, 0, 0);

    public static bool IsCompatible(Version minHostVersion, Version? maxHostVersion, Version hostVersion)
    {
        return hostVersion >= minHostVersion &&
               (maxHostVersion is null || hostVersion <= maxHostVersion);
    }

    /// <summary>
    /// Evaluates raw attribute values as stored on the assembly. Unparsable bounds behave as
    /// absent, and the check never throws: a malformed version must read as incompatible with
    /// nothing rather than crash the scan.
    /// </summary>
    public static bool IsCompatibleWithBounds(Version? hostVersion, string? minHostVersion, string? maxHostVersion)
    {
        if (hostVersion is null)
        {
            return false;
        }

        try
        {
            var min = TryParseBound(minHostVersion) ?? DefaultMinHostVersion;
            var max = TryParseBound(maxHostVersion);
            return IsCompatible(min, max, hostVersion);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Parses one raw version bound; returns null when the value is absent or malformed.</summary>
    public static Version? TryParseBound(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Version.TryParse(value.Trim(), out var parsed) ? parsed : null;
    }
}
