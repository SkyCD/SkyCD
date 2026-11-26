using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace SkyCD.Plugin.Metadata.Video.Parsing;

/// <summary>
/// Loads the OpenCvSharp native runtime from the directory that holds the managed binding.
/// </summary>
/// <remarks>
/// Plugin assemblies are loaded with <c>Assembly.LoadFrom</c> rather than through the host's
/// <c>deps.json</c>, so the default P/Invoke probing does not reach <c>OpenCvSharpExtern.dll</c>. The build
/// places both native libraries next to <c>OpenCvSharp.dll</c>; preloading them by full path makes the later
/// name-based P/Invoke and the videoio backend's dynamic FFmpeg load resolve to those exact files. Failure is
/// reported rather than thrown, because a platform without the Windows runtime is a metadata-only outcome.
/// </remarks>
internal static class OpenCvNativeBootstrap
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

        try
        {
            // Resolving the managed binding touches typeof(VideoCapture): when OpenCvSharp.dll is
            // not deployed this throws at type-load time, which must read as "decoder unavailable"
            // (issue #1202) instead of escaping the capability as a provider failure.
            var directory = ResolveNativeDirectory();
            if (directory is null)
            {
                return false;
            }

            foreach (var ffmpeg in Directory.EnumerateFiles(directory, "opencv_videoio_ffmpeg*.dll"))
            {
                NativeLibrary.Load(ffmpeg);
            }

            NativeLibrary.Load(Path.Combine(directory, "OpenCvSharpExtern.dll"));
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException
                                              or IOException or UnauthorizedAccessException or ArgumentException
                                              or FileNotFoundException or FileLoadException or TypeLoadException
                                              or MissingMethodException)
        {
            return false;
        }
    }

    /// <summary>
    /// Prefers the plugin output root, where the project links both DLLs; falls back to the package's
    /// <c>runtimes/</c> layout, which is what a test or tool output that only restores the package contains.
    /// </summary>
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
