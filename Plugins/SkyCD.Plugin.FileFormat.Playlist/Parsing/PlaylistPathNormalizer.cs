using System;
using System.Runtime.InteropServices;

namespace SkyCD.Plugin.FileFormat.Playlist.Parsing;

internal static class PlaylistPathNormalizer
{
    public static string NormalizeFileUri(Uri fileUri)
    {
        var localPath = Uri.UnescapeDataString(fileUri.LocalPath);

        // On Unix-based systems, Windows drive-letter file URIs decode as "/C:/...".
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
            localPath.Length >= 4 &&
            localPath[0] == '/' &&
            char.IsLetter(localPath[1]) &&
            localPath[2] == ':' &&
            localPath[3] == '/')
        {
            localPath = localPath[1..];
        }

        if (localPath.Length >= 3 &&
            char.IsLetter(localPath[0]) &&
            localPath[1] == ':' &&
            localPath[2] == '/')
        {
            localPath = localPath.Replace('/', '\\');
        }

        return localPath;
    }
}
