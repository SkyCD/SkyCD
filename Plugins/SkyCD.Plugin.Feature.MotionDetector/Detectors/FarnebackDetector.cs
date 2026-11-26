using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Detectors;

/// <summary>Dense Farnebäck optical flow: frame pixels whose flow magnitude exceeds a threshold count as motion.</summary>
internal sealed class FarnebackDetector(DetectorParameters parameters) : IMotionDetector
{
    private const int WorkingWidth = 320;

    private readonly double magnitudeThreshold = parameters.Get("magnitudeThreshold", 2, 0.1, 50);
    private readonly double minimumAreaPercent = parameters.Get("minAreaPercent", 0.2, 0, 100);
    private Mat? previous;
    private Mat? flow;
    private Mat? magnitude;
    private Mat? angle;
    private Mat? mask;

    public string Id => DetectorCatalog.Farneback;

    public DetectionOutcome Process(Mat frame, Mat gray)
    {
        using var small = Shrink(gray);
        if (previous is null || previous.Size() != small.Size())
        {
            previous?.Dispose();
            previous = small.Clone();
            return new DetectionOutcome(false, 0, Array.Empty<Rect>());
        }

        flow ??= new Mat();
        magnitude ??= new Mat();
        angle ??= new Mat();
        mask ??= new Mat();
        Cv2.CalcOpticalFlowFarneback(previous, small, flow, 0.5, 3, 15, 3, 5, 1.2, 0);
        small.CopyTo(previous);
        var planes = flow.Split();
        try
        {
            Cv2.CartToPolar(planes[0], planes[1], magnitude, angle);
        }
        finally
        {
            foreach (var plane in planes)
            {
                plane.Dispose();
            }
        }

        Cv2.Threshold(magnitude, mask, magnitudeThreshold, 255, ThresholdTypes.Binary);
        var outcome = DetectionRegions.FromMask(mask, minimumAreaPercent);
        return scaleX == 1 && scaleY == 1
            ? outcome
            : new DetectionOutcome(outcome.MotionDetected, outcome.Score, ScaleRegions(outcome.Regions));
    }

    private double scaleX = 1;
    private double scaleY = 1;

    private Mat Shrink(Mat gray)
    {
        if (gray.Width <= WorkingWidth)
        {
            scaleX = 1;
            scaleY = 1;
            return gray.Clone();
        }

        var width = WorkingWidth;
        var height = Math.Max(1, (int)Math.Round(gray.Height * (WorkingWidth / (double)gray.Width)));
        scaleX = gray.Width / (double)width;
        scaleY = gray.Height / (double)height;
        var small = new Mat();
        Cv2.Resize(gray, small, new Size(width, height), interpolation: InterpolationFlags.Area);
        return small;
    }

    private IReadOnlyList<Rect> ScaleRegions(IReadOnlyList<Rect> regions)
    {
        var scaled = new Rect[regions.Count];
        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            scaled[index] = new Rect(
                (int)Math.Round(region.X * scaleX),
                (int)Math.Round(region.Y * scaleY),
                Math.Max(1, (int)Math.Round(region.Width * scaleX)),
                Math.Max(1, (int)Math.Round(region.Height * scaleY)));
        }

        return scaled;
    }

    public void Dispose()
    {
        previous?.Dispose();
        flow?.Dispose();
        magnitude?.Dispose();
        angle?.Dispose();
        mask?.Dispose();
    }
}
