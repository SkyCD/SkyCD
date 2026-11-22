using System;
using SkyCD.Plugin.MediaMetadata;
using SkyCD.Plugin.MediaMetadata.Parsing;

namespace SkyCD.Plugin.Metadata.FileMetadata.Parsing;

/// <summary>Dispatches to the reader of the operating system the host runs on.</summary>
internal static class OsMetadataReader
{
    public static MetadataScan Read(string path)
    {
        var scan = new MetadataScan();
        if (OperatingSystem.IsWindows())
        {
            WindowsPropertyReader.Read(path, scan);
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacXattrReader.Read(path, scan);
        }
        else if (OperatingSystem.IsLinux())
        {
            LinuxXattrReader.Read(path, scan);
        }

        return scan;
    }
}
