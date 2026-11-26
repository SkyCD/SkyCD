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
/// default P/Invoke probing does not reach the platform library. The build places it next to
/// <c>OpenCvSharp.dll</c>; preloading it by full path makes the later name-based P/Invoke resolve to that exact
/// file. Linux packages also need their GTK stack for highgui, so a missing system library is reported as an
/// unavailable runtime rather than an exception.
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

    /// <summary>The native library file name OpenCvSharp expects on the current platform.</summary>
    internal static string LibraryFileName =>
        OperatingSystem.IsWindows() ? "OpenCvSharpExtern.dll"
        : OperatingSystem.IsMacOS() ? "libOpenCvSharpExtern.dylib"
        : "libOpenCvSharpExtern.so";

    private static bool TryLoad()
    {
        var directory = ResolveNativeDirectory();
        if (directory is null)
        {
            return false;
        }

        try
        {
            // Windows ships the FFmpeg videoio backend as a separate DLL; other platforms bundle it.
            foreach (var dependency in Directory.EnumerateFiles(directory, "*opencv_videoio_ffmpeg*"))
            {
                NativeLibrary.Load(dependency);
            }

            NativeLibrary.Load(Path.Combine(directory, LibraryFileName));
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

        if (File.Exists(Path.Combine(managedDirectory, LibraryFileName)))
        {
            return managedDirectory;
        }

        // RID-less outputs keep package native assets under runtimes/<rid>/native.
        var runtimeDirectory = Path.Combine(managedDirectory, "runtimes",
            RuntimeInformation.RuntimeIdentifier, "native");
        if (File.Exists(Path.Combine(runtimeDirectory, LibraryFileName)))
        {
            return runtimeDirectory;
        }

        var runtimesDirectory = Path.Combine(managedDirectory, "runtimes");
        if (!Directory.Exists(runtimesDirectory))
        {
            return null;
        }

        foreach (var directory in Directory.EnumerateDirectories(runtimesDirectory, "native",
                     SearchOption.AllDirectories))
        {
            if (File.Exists(Path.Combine(directory, LibraryFileName)))
            {
                return directory;
            }
        }

        return null;
    }
}
