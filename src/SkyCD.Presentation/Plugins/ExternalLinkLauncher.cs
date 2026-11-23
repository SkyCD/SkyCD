using System;
using System.Diagnostics;

namespace SkyCD.Presentation.Plugins;

/// <summary>
/// Opens externally supplied links through the shell, but only for absolute http(s) URLs.
/// Plugin manifests are third-party data: a <c>file://</c>, UNC or custom-protocol value would let the
/// shell launch local content or a registered handler, so those schemes are rejected before launch.
/// </summary>
public static class ExternalLinkLauncher
{
    public static bool IsLaunchable(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static bool TryOpen(string? value)
    {
        return TryOpen(value, static startInfo =>
        {
            using var process = Process.Start(startInfo);
        });
    }

    internal static bool TryOpen(string? value, Action<ProcessStartInfo> launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        if (!IsLaunchable(value))
        {
            return false;
        }

        try
        {
            launcher(new ProcessStartInfo { FileName = value, UseShellExecute = true });
            return true;
        }
        catch
        {
            // The shell may refuse the scheme or have no default handler; a failed link must not crash Options.
            return false;
        }
    }
}
