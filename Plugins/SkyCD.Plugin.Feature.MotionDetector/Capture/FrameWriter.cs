using System;
using System.Globalization;
using System.IO;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Capture;

/// <summary>Writes captured frames into the source's physical destination folder without half-written files.</summary>
internal static class FrameWriter
{
    private static readonly ImageEncodingParam[] JpegQuality =
        [new(ImwriteFlags.JpegQuality, 90)];

    /// <summary>Writes one frame and returns its full path, or null when the write failed.</summary>
    public static string? TryWrite(Mat frame, string destinationFolder, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            return null;
        }

        var temporary = string.Empty;
        try
        {
            var folder = Path.Combine(destinationFolder, capturedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            var name = string.Create(CultureInfo.InvariantCulture,
                $"{capturedAt:HHmmss-fff}-{Guid.NewGuid():N}.jpg");
            var target = Path.Combine(folder, name);
            temporary = target + ".tmp";
            if (!Cv2.ImWrite(temporary, frame, JpegQuality))
            {
                return null;
            }

            File.Move(temporary, target);
            return target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or OpenCVException or ArgumentException
                                              or NotSupportedException)
        {
            if (!string.IsNullOrEmpty(temporary))
            {
                try { File.Delete(temporary); } catch (Exception) { /* Best effort. */ }
            }

            return null;
        }
    }
}
