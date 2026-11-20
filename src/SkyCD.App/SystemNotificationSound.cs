using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SkyCD.App;

/// <summary>
/// Plays the operating system's notification sound on every supported desktop platform without adding packages:
/// the Windows system beep, the macOS system sound through <c>afplay</c>, and Linux through the freedesktop
/// sound theme when a player is installed. Calls are best-effort and never throw for missing players or themes.
/// </summary>
internal static class SystemNotificationSound
{
    private const uint AsteriskBeep = 0x00000040;

    public static void Play()
    {
        if (OperatingSystem.IsWindows())
        {
            // Prefer the "asterisk" sound configured in Windows; fall back to the plain beep.
            _ = MessageBeep(AsteriskBeep);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            TryStartProcess("/usr/bin/afplay", ["/System/Library/Sounds/Glass.aiff"]);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            // canberra-gtk-play ships with libcanberra, which every freedesktop sound theme relies on.
            if (TryStartProcess("/usr/bin/canberra-gtk-play", ["--id=complete"]))
            {
                return;
            }

            TryStartProcess("/usr/bin/paplay", ["/usr/share/sounds/freedesktop/stereo/complete.oga"]);
        }
    }

    private static bool TryStartProcess(string fileName, string[] arguments)
    {
        if (!System.IO.File.Exists(fileName))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            // Drain the redirected streams so a paused player never blocks on a full pipe.
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            return true;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                              or InvalidOperationException
                                              or System.IO.FileNotFoundException)
        {
            return false;
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBeep")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MessageBeep(uint beepType);
}
