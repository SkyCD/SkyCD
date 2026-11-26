using System;
using System.Collections.Generic;
using OpenCvSharp;
using SkyCD.Plugin.Feature.MotionDetector.Configuration;
using SkyCD.Plugin.Feature.MotionDetector.Models;
using SkyCD.SDK.Capabilities.Capture;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.MotionDetector.Capture;

/// <summary>
/// Enumerates local capture devices by probing indexes. OpenCV has no portable device list, so each index is
/// opened and read once; the result is cached briefly because probing takes a moment per camera.
/// </summary>
internal static class DeviceEnumerator
{
    private const int ProbingLimit = 5;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);
    private static readonly object Gate = new();
    private static DateTimeOffset cachedAt = DateTimeOffset.MinValue;
    private static IReadOnlyList<CaptureDevice> cached = [];

    public static IReadOnlyList<CaptureDevice> Enumerate()
    {
        lock (Gate)
        {
            if (DateTimeOffset.UtcNow - cachedAt < CacheLifetime)
            {
                return cached;
            }

            cached = Probe();
            cachedAt = DateTimeOffset.UtcNow;
            return cached;
        }
    }

    private static IReadOnlyList<CaptureDevice> Probe()
    {
        if (!NativeOpenCv.EnsureLoaded())
        {
            return [];
        }

        var devices = new List<CaptureDevice>();
        for (var index = 0; index < ProbingLimit; index++)
        {
            try
            {
                using var capture = new VideoCapture(index,
                    OperatingSystem.IsWindows() ? VideoCaptureAPIs.DSHOW : VideoCaptureAPIs.ANY);
                if (capture.IsOpened() && capture.Grab())
                {
                    devices.Add(new CaptureDevice(
                        index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        LocalizationService.Current.Format(MotionDetectorKeys.DeviceName, index + 1)));
                }
            }
            catch (Exception exception) when (exception is OpenCVException or DllNotFoundException
                                                  or BadImageFormatException or ArgumentException)
            {
                // A missing native runtime or an unsupported backend ends enumeration for every index.
                break;
            }
        }

        return devices;
    }
}
