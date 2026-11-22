using System;
using System.Threading;
using SkyCD.Plugin.Metadata.AudioCD.Models;

namespace SkyCD.Plugin.Metadata.AudioCD.Parsing;

/// <summary>
/// Reads an audio CD table of contents with the reader of the current operating system. Unsupported platforms
/// report the drive as unidentified, so the plugin still loads everywhere and simply captures nothing.
/// </summary>
public sealed class PlatformDiscTocReader : IDiscTocReader
{
    private readonly WindowsDiscTocReader windows = new();
    private readonly LinuxDiscTocReader linux = new();
    private readonly MacDiscTocReader mac = new();

    public DiscToc? Read(string rootPath, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return windows.Read(rootPath, cancellationToken);
        }

        if (OperatingSystem.IsLinux())
        {
            return linux.Read(rootPath, cancellationToken);
        }

        return OperatingSystem.IsMacOS() ? mac.Read(rootPath, cancellationToken) : null;
    }
}
