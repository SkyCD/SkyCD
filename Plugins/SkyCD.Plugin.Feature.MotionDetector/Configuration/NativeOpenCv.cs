using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Configuration;

/// <summary>
/// Loads the OpenCvSharp native runtime from the directory that holds the managed binding.
/// </summary>
/// <remarks>
/// Plugin assemblies are loaded with <c>Assembly.LoadFrom</c> rather than through the host's deps.json, so the
/// default P/Invoke probing does not reach <c>OpenCvSharpExtern.dll</c>. The build places both native libraries
/// next to <c>OpenCvSharp.dll</c>; preloading them by full path makes the later name-based P/Invoke and the
/// videoio backend's dynamic FFmpeg load resolve to those exact files.
/// </remarks>
internal static class NativeOpenCv
{
    private static readonly object Gate = new();
    private static bool? available;

    public static bool EnsureLoaded()
    {
        lock (Gate)
        {
            return available ??= TryLoad();
        }
    }

    private static bool TryLoad()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var directory = ResolveNativeDirectory();
        if (directory is null)
        {
            return false;
        }

        try
        {
            foreach (var ffmpeg in Directory.EnumerateFiles(directory, "opencv_videoio_ffmpeg*.dll"))
            {
                NativeLibrary.Load(ffmpeg);
            }

            NativeLibrary.Load(Path.Combine(directory, "OpenCvSharpExtern.dll"));
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException
                                              or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static string? ResolveNativeDirectory()
    {
        var managedDirectory = Path.GetDirectoryName(typeof(VideoCapture).Assembly.Location);
        if (string.IsNullOrEmpty(managedDirectory))
        {
            return null;
        }

        if (File.Exists(Path.Combine(managedDirectory, "OpenCvSharpExtern.dll")))
        {
            return managedDirectory;
        }

        var runtimeDirectory = Path.Combine(managedDirectory, "runtimes", "win-x64", "native");
        return File.Exists(Path.Combine(runtimeDirectory, "OpenCvSharpExtern.dll")) ? runtimeDirectory : null;
    }
}
