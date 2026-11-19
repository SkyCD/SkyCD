using System;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Converts between the seam's path model (<c>/share/folder</c>) and Windows UNC paths
/// (<c>\\server\share\folder</c>) for the SMB provider (#626).
/// </summary>
internal static class SmbPath
{
    /// <summary>Builds the UNC path of a share or one of its children.</summary>
    public static string ToUnc(RemoteLocation location, string path)
    {
        var normalized = FtpPath.Normalize(path);
        var trimmed = normalized.Trim('/');
        if (trimmed.Length == 0)
        {
            return $@"\\{location.Host}";
        }

        var parts = trimmed.Split('/');
        var unc = $@"\\{location.Host}\{parts[0]}";
        for (var i = 1; i < parts.Length; i++)
        {
            unc += "\\" + parts[i];
        }

        return unc;
    }

    /// <summary>
    /// Maps a UNC path back onto the seam's path model. Targets on the scanned server collapse
    /// to <c>/share/…</c> so they compare equal to listed paths in the shared visited set;
    /// targets on other servers keep their host as the first segment so they never collide.
    /// </summary>
    public static string ToRemotePath(string uncPath, string host)
    {
        var path = (uncPath ?? string.Empty).Replace('\\', '/');
        if (!path.StartsWith("//", StringComparison.Ordinal))
        {
            // Not a UNC path (for example a test double's local path): normalize it directly.
            return FtpPath.Normalize(Uri.UnescapeDataString(path));
        }

        var segments = path[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Equals(host, StringComparison.OrdinalIgnoreCase))
        {
            return FtpPath.Normalize("/" + string.Join("/", segments[1..]));
        }

        // Another host keeps its name as the first segment and the "//" prefix so the
        // identity can never collide with a path of the scanned server.
        return "//" + string.Join("/", segments);
    }
}
