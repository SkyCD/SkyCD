using System;
using System.IO;
using System.Threading;
using OpenCvSharp;
using SkyCD.Plugin.Feature.MotionDetector.Configuration;
using SkyCD.Plugin.Feature.MotionDetector.Models;
using SkyCD.SDK.Capabilities.Sources;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.MotionDetector.Capture;

/// <summary>Opens the OpenCV capture for one configured source kind and owns the native handle.</summary>
internal sealed class CaptureConnection : IDisposable
{
    private CaptureConnection(VideoCapture capture)
    {
        Capture = capture;
    }

    public VideoCapture Capture { get; }

    private int disposed;

    public static bool TryOpen(SourceConfiguration source, out CaptureConnection? connection,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(source);
        connection = null;
        error = null;

        if (!NativeOpenCv.EnsureLoaded())
        {
            error = LocalizationService.Current.Get(MotionDetectorKeys.NativeUnavailable);
            return false;
        }

        VideoCapture? capture = null;
        try
        {
            capture = new VideoCapture();
            if (source.KindId == CaptureSourceKinds.Device)
            {
                if (!int.TryParse(source.Location, out var index))
                {
                    error = LocalizationService.Current.Get(MotionDetectorKeys.SourceOpenFailed);
                    return false;
                }

                capture.Open(index, OperatingSystem.IsWindows() ? VideoCaptureAPIs.DSHOW : VideoCaptureAPIs.ANY);
            }
            else if (CaptureSourceKinds.IsUrl(source.KindId))
            {
                if (!Uri.TryCreate(source.Location, UriKind.Absolute, out _))
                {
                    error = LocalizationService.Current.Get(MotionDetectorKeys.SourceUrlInvalid);
                    return false;
                }

                capture.Open(source.Location, VideoCaptureAPIs.ANY);
            }
            else
            {
                if (!File.Exists(source.Location))
                {
                    error = LocalizationService.Current.Get(MotionDetectorKeys.SourceFileMissing);
                    return false;
                }

                capture.Open(source.Location, VideoCaptureAPIs.ANY);
            }

            if (!capture.IsOpened())
            {
                error = LocalizationService.Current.Get(MotionDetectorKeys.SourceOpenFailed);
                return false;
            }

            connection = new CaptureConnection(capture);
            capture = null;
            return true;
        }
        catch (Exception exception) when (exception is OpenCVException or ArgumentException
                                              or DllNotFoundException or BadImageFormatException)
        {
            error = LocalizationService.Current.Get(MotionDetectorKeys.SourceOpenFailed);
            return false;
        }
        finally
        {
            capture?.Dispose();
        }
    }

    /// <summary>Milliseconds between reads; a source that reports no usable frame rate is polled at 10 fps.</summary>
    public TimeSpan FrameInterval
    {
        get
        {
            var fps = Capture.Fps;
            if (!double.IsFinite(fps) || fps <= 0.5 || fps > 120)
            {
                return TimeSpan.FromMilliseconds(100);
            }

            return TimeSpan.FromMilliseconds(Math.Max(30, 1000 / fps));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            Capture.Dispose();
        }
    }
}
