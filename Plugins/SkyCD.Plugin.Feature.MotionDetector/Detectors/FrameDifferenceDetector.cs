using System;
using OpenCvSharp;

namespace SkyCD.Plugin.Feature.MotionDetector.Detectors;

/// <summary>Absolute per-pixel difference against the previous frame, thresholded into a binary mask.</summary>
internal sealed class FrameDifferenceDetector(DetectorParameters parameters) : IMotionDetector
{
    private readonly double threshold = parameters.Get("threshold", 25, 1, 255);
    private readonly double minimumAreaPercent = parameters.Get("minAreaPercent", 0.2, 0, 100);
    private Mat? previous;
    private Mat? difference;
    private Mat? mask;

    public string Id => DetectorCatalog.FrameDifference;

    public DetectionOutcome Process(Mat frame, Mat gray)
    {
        if (previous is null || previous.Size() != gray.Size())
        {
            Replace(ref previous, gray);
            return new DetectionOutcome(false, 0, Array.Empty<Rect>());
        }

        difference ??= new Mat();
        mask ??= new Mat();
        Cv2.Absdiff(previous, gray, difference);
        gray.CopyTo(previous);
        Cv2.Threshold(difference, mask, threshold, 255, ThresholdTypes.Binary);
        return DetectionRegions.FromMask(mask, minimumAreaPercent);
    }

    private static void Replace(ref Mat? target, Mat source)
    {
        target?.Dispose();
        target = source.Clone();
    }

    public void Dispose()
    {
        previous?.Dispose();
        difference?.Dispose();
        mask?.Dispose();
    }
}
