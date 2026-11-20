using System;
using SkyCD.Presentation.Scanning;

namespace SkyCD.App;

/// <summary>
/// Ejects the scanned medium through the operating system. Ejection is best-effort: unsupported platforms and
/// drives report <see cref="MediaEjectionOutcome.PlatformUnsupported"/> or <see cref="MediaEjectionOutcome.Failed"/>
/// instead of throwing, so a finished scan never ends in an error because of it.
/// </summary>
public sealed class SystemMediaEjector : IMediaEjector
{
    public bool IsPlatformSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    public MediaEjectionSupport GetSupport(string? source)
    {
        if (!IsPlatformSupported)
        {
            return MediaEjectionSupport.PlatformUnsupported;
        }

        return IsEjectableSource(source)
            ? MediaEjectionSupport.Supported
            : MediaEjectionSupport.SourceUnsupported;
    }

    public MediaEjectionOutcome Eject(string? source)
    {
        if (!IsPlatformSupported)
        {
            return MediaEjectionOutcome.PlatformUnsupported;
        }

        if (!IsEjectableSource(source))
        {
            return MediaEjectionOutcome.NotApplicable;
        }

        try
        {
            var ejected = OperatingSystem.IsWindows()
                ? WindowsOpticalEjector.TryEject(NormalizeWindowsDriveRoot(source!))
                : UnixOpticalEjector.TryEject(source!);
            return ejected ? MediaEjectionOutcome.Ejected : MediaEjectionOutcome.Failed;
        }
        catch (Exception exception)
        {
            // Drivers answer with arbitrary platform exceptions; a finished scan must not surface them.
            _ = exception;
            return MediaEjectionOutcome.Failed;
        }
    }

    /// <summary>
    /// Only drive-shaped sources are ejectable: a Windows drive root ("E:", "E:\") or a Unix optical device node
    /// ("/dev/sr0", "/dev/rdiskN"). Folders and network locations never are.
    /// </summary>
    public static bool IsEjectableSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var trimmed = source.Trim();
        if (OperatingSystem.IsWindows())
        {
            return trimmed.Length is 2 or 3 &&
                   char.IsAsciiLetter(trimmed[0]) &&
                   trimmed[1] == ':' &&
                   (trimmed.Length == 2 || trimmed[2] is '\\' or '/');
        }

        return trimmed.StartsWith("/dev/", StringComparison.Ordinal) &&
               !trimmed.Contains("..", StringComparison.Ordinal) &&
               trimmed.Length > "/dev/".Length;
    }

    internal static string NormalizeWindowsDriveRoot(string source)
    {
        var trimmed = source.Trim();
        return trimmed.Length == 2 ? trimmed + "\\" : trimmed;
    }
}
