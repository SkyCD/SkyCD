using System.Collections.Generic;

namespace SkyCD.Plugin.Feature.MotionDetector.Models;

/// <summary>Stable identifiers of the source kinds this plugin opens.</summary>
public static class CaptureSourceKinds
{
    public const string File = "file";
    public const string Device = "device";
    public const string JpegUrl = "jpeg-url";
    public const string MjpegUrl = "mjpeg-url";
    public const string MmsStream = "mms-stream";

    /// <summary>Gets whether the kind location is a URL rather than a file path or device index.</summary>
    public static bool IsUrl(string kindId) => kindId is JpegUrl or MjpegUrl or MmsStream;

    /// <summary>Gets the allowed image extensions a destination folder watcher imports.</summary>
    public static IReadOnlyList<string> ImageExtensions { get; } = [".jpg", ".jpeg", ".png", ".bmp"];
}
