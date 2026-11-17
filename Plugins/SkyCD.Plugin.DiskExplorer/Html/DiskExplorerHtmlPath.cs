using System;
using System.IO;

namespace SkyCD.Plugin.DiskExplorer.Html;

/// <summary>
/// Link and asset path safety for HTML report bundles. Only plain relative paths
/// contained within the report root are resolvable: absolute, UNC, drive-qualified
/// and parent-escaping paths, remote URLs, and symlink/reparse-point escapes are
/// rejected. Nothing is ever fetched from the network and nothing executes.
/// </summary>
public static class DiskExplorerHtmlPath
{
    public static bool IsRemoteOrSpecialScheme(string href)
    {
        var trimmed = href.Trim();
        var colon = trimmed.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        var scheme = trimmed[..colon];
        foreach (var character in scheme)
        {
            if (!(char.IsLetterOrDigit(character) || character is '+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryResolveWithinRoot(
        string reportRootDirectory,
        string pageDirectory,
        string href,
        out string fullPath,
        out string failure)
    {
        fullPath = string.Empty;
        failure = string.Empty;

        var reference = href.Trim();
        if (reference.Length == 0 || reference.StartsWith('#'))
        {
            failure = "empty or fragment-only reference";
            return false;
        }

        var pathPart = reference.Split(['#', '?'], 2)[0];
        if (string.IsNullOrWhiteSpace(pathPart))
        {
            failure = "empty path";
            return false;
        }

        if (IsRemoteOrSpecialScheme(pathPart))
        {
            failure = $"remote or special scheme is never fetched: '{pathPart}'";
            return false;
        }

        var normalized = pathPart.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)
            || normalized.StartsWith(@"\\", StringComparison.Ordinal)
            || (normalized.Length >= 2 && normalized[1] == ':'))
        {
            failure = $"absolute path escapes the report bundle: '{pathPart}'";
            return false;
        }

        string combined;
        try
        {
            combined = Path.GetFullPath(Path.Combine(pageDirectory, normalized));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            failure = $"unresolvable path: '{pathPart}'";
            return false;
        }

        var root = Path.GetFullPath(reportRootDirectory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            failure = $"path escapes the report bundle: '{pathPart}'";
            return false;
        }

        fullPath = combined;
        return true;
    }

    public static bool IsReparsePoint(string fullPath)
    {
        try
        {
            var attributes = File.Exists(fullPath)
                ? File.GetAttributes(fullPath)
                : Directory.Exists(fullPath)
                    ? File.GetAttributes(fullPath)
                    : (FileAttributes)0;
            return (attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return true;
        }
    }

    public static string RelativeToRoot(string reportRootDirectory, string fullPath)
    {
        var root = Path.GetFullPath(reportRootDirectory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var relative = Path.GetRelativePath(root, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }
}
